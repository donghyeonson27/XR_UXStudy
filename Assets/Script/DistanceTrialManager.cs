using System.Collections;
using System.IO;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public enum DistanceCondition { Auto, Gesture }

//작품 하나에 필요한 것들
[System.Serializable]
public class DistanceArtwork
{
    public string label; //지시문에 쓸 이름 (예 : 빨간)
    public Transform body; //작품 오브젝트
    public Renderer renderer; //작품 색
    public HandGrabInteractable nearGrab; //근접 잡기 (ISDK_HandGrabInteraction)
    public DistanceHandGrabInteractable farGrab; //원거리 잡기 (ISDK_DistanceHandGrabInteraction)
}

public class DistanceTrialManager : MonoBehaviour
{
    //참가자 설정
    [SerializeField] private int participantNumber = 1; //기록용 참가자 번호
    [SerializeField] private float restTime = 1.5f; //과제 사이 쉬는 시간

    //연결할 오브젝트
    [SerializeField] private Transform station; //GrabStation (테이블 전체)
    [SerializeField] private Transform targetPoint; //옮길 위치 (노란 표시)
    [SerializeField] private Renderer targetRenderer; //노란 표시 색
    [SerializeField] private TMP_Text prompt; //지시문
    [SerializeField] private Transform head; //CenterEyeAnchor

    //작품 4개 (순서 : 0 가까운 왼쪽, 1 가까운 오른쪽, 2 먼 왼쪽, 3 먼 오른쪽)
    [SerializeField] private DistanceArtwork[] artworks = new DistanceArtwork[4];

    //배치 (머리 기준 수평 거리)
    [SerializeField] private float nearDistance = 0.45f; //가까운 작품 거리
    [SerializeField] private float farDistance = 1.2f; //먼 작품 거리
    [SerializeField] private float sideAngle = 20f; //왼쪽 / 오른쪽 각도
    [SerializeField] private float stationDrop = 0.3f; //테이블 윗면이 눈높이보다 낮은 정도
    [SerializeField] private float farLift = 0.08f; //먼 작품을 가까운 작품 뒤로 살짝 보이게 올리는 값

    //성공 판정
    [SerializeField] private float targetRadius = 0.05f; //노란 표시 반지름
    [SerializeField] private float targetHeight = 0.1f; //표시 위 10cm 안에서 놓으면 성공

    //블록 순서 (ABBA : 자동 → 손 모양 → 손 모양 → 자동)
    private DistanceCondition[] order = { DistanceCondition.Auto, DistanceCondition.Gesture, DistanceCondition.Gesture, DistanceCondition.Auto };

    //블록마다 옮길 작품 순서 (가까운 것 / 먼 것 번갈아)
    //0, 3블록은 첫 번째, 1, 2블록은 두 번째 순서 → 두 조건 모두 가까운 것 3번, 먼 것 3번
    private int[][] targetOrders = { new[] { 0, 3, 1 }, new[] { 2, 1, 3 } };

    //진행 상태
    private int block = -1; //몇 번째 블록인지 (-1 : 시작 전, 0 ~ 3)
    private bool blockRunning; //블록 진행 중인지
    private bool measuring; //과제 측정 중인지

    //과제 기록
    private int trial; //블록 안에서 몇 번째 과제인지
    private int target; //이번에 옮길 작품 번호
    private int wrongGrabs; //다른 작품을 잡은 횟수
    private float startTime; //지시문이 뜬 시간
    private bool[] wasSelected = new bool[4]; //지난 프레임에 잡고 있었는지 (작품마다)
    private Vector3[] slots = new Vector3[4]; //작품마다 놓일 자리
    private Color[] baseColors = new Color[4]; //작품 원래 색
    private Color targetColor; //노란 표시 원래 색
    private string logPath; //CSV 저장 위치

    void Start()
    {
        //CSV 파일이 없으면 첫 줄(항목 이름) 만들기
        logPath = Path.Combine(Application.persistentDataPath, "distance_log.csv");
        if (!File.Exists(logPath))
            File.AppendAllText(logPath, "participant,block,condition,trial,target,distance,timeSec,wrongGrabs\n");
        Debug.Log("저장 위치 : " + logPath);

        for (int i = 0; i < artworks.Length; i++)
            baseColors[i] = artworks[i].renderer.material.color;

        targetColor = targetRenderer.material.color;
        SetGrabbable(false);
        prompt.text = "준비";
    }

    void Update()
    {
        if (Keyboard.current != null)
        {
            //1, 2 : 연습 (기록 안 함)
            if (Keyboard.current.digit1Key.wasPressedThisFrame && !blockRunning)
                StartPractice(DistanceCondition.Auto);
            if (Keyboard.current.digit2Key.wasPressedThisFrame && !blockRunning)
                StartPractice(DistanceCondition.Gesture);

            //Space : 첫 블록 시작
            if (Keyboard.current.spaceKey.wasPressedThisFrame && block == -1)
                BeginBlock(0);

            //N : 블록이 끝난 뒤 다음 블록 시작
            bool hasNextBlock = block >= 0 && block < order.Length - 1;
            if (Keyboard.current.nKey.wasPressedThisFrame && hasNextBlock && !blockRunning)
                BeginBlock(block + 1);
        }

        UpdateArtworkColors();

        if (measuring)
            CheckArtworks();
    }

    //작품을 잡을 수 있게 / 없게
    void SetGrabbable(bool canGrab)
    {
        foreach (DistanceArtwork artwork in artworks)
        {
            artwork.nearGrab.gameObject.SetActive(canGrab);
            artwork.farGrab.gameObject.SetActive(canGrab);
        }
    }

    //잡기 방식 고르는 법 정하기
    void SetCondition(DistanceCondition condition)
    {
        foreach (DistanceArtwork artwork in artworks)
        {
            if (condition == DistanceCondition.Auto)
            {
                //자동 : 어떤 손 모양이든 거리에 따라 근접 / 원거리가 알아서 정해짐
                artwork.nearGrab.InjectSupportedGrabTypes(GrabTypeFlags.All);
                artwork.farGrab.InjectSupportedGrabTypes(GrabTypeFlags.All);
            }
            else
            {
                //손 모양 : 손바닥으로 쥐면 근접, 핀치하면 원거리
                artwork.nearGrab.InjectSupportedGrabTypes(GrabTypeFlags.Palm);
                artwork.farGrab.InjectSupportedGrabTypes(GrabTypeFlags.Pinch);
            }
        }

        SetGrabbable(true);
    }

    //작품을 잡고 있는지 (근접 / 원거리 둘 중 하나라도)
    bool IsSelected(int i)
    {
        return artworks[i].nearGrab.State == InteractableState.Select
            || artworks[i].farGrab.State == InteractableState.Select;
    }

    //손이 향하거나 잡으면 원래 색보다 밝게 (두 조건 같은 피드백)
    void UpdateArtworkColors()
    {
        for (int i = 0; i < artworks.Length; i++)
        {
            DistanceArtwork artwork = artworks[i];
            bool hover = artwork.nearGrab.State == InteractableState.Hover
                || artwork.farGrab.State == InteractableState.Hover;

            if (artwork.nearGrab.gameObject.activeInHierarchy && (hover || IsSelected(i)))
                artwork.renderer.material.color = Color.Lerp(baseColors[i], Color.white, 0.4f);
            else
                artwork.renderer.material.color = baseColors[i];
        }
    }

    //연습 : 자유롭게 옮겨보기 (기록 안 함)
    void StartPractice(DistanceCondition condition)
    {
        PlaceStation();
        ResetAllArtworks();
        SetCondition(condition);
        prompt.text = "연습 : 자유롭게 옮겨 보세요";
    }

    //블록 시작
    void BeginBlock(int index)
    {
        block = index;
        PlaceStation();
        SetCondition(order[block]);

        trial = 0;
        blockRunning = true;
        NextTrial();
    }

    //테이블과 작품 자리를 머리 위치 기준으로 정하기
    void PlaceStation()
    {
        Vector3 forward = head.forward;
        forward.y = 0; //고개를 숙이고 있어도 수평 방향 기준으로
        forward.Normalize();

        Vector3 headFloor = new Vector3(head.position.x, 0, head.position.z); //머리의 수평 위치
        float tableY = head.position.y - stationDrop; //테이블 윗면 높이

        station.position = new Vector3(head.position.x, tableY, head.position.z) + forward * nearDistance;
        station.rotation = Quaternion.LookRotation(forward);
        targetPoint.position = station.position; //노란 표시 = 테이블 가운데

        for (int side = 0; side < 2; side++)
        {
            float angle = side == 0 ? -sideAngle : sideAngle; //0 왼쪽, 1 오른쪽
            Vector3 dir = Quaternion.Euler(0, angle, 0) * forward;
            float half = artworks[side].body.localScale.y * 0.5f;

            //가까운 작품 : 테이블 위
            Vector3 near = headFloor + dir * nearDistance;
            near.y = tableY + half;
            slots[side] = near;

            //먼 작품 : 눈 → 가까운 작품을 이은 선 위, 살짝 위로
            float drop = (head.position.y - near.y) * (farDistance / nearDistance);
            Vector3 far = headFloor + dir * farDistance;
            far.y = head.position.y - drop + farLift;
            slots[side + 2] = far;
        }
    }

    //작품 하나를 원래 자리로
    void ResetArtwork(int i)
    {
        artworks[i].body.position = slots[i];
        artworks[i].body.rotation = station.rotation;
    }

    void ResetAllArtworks()
    {
        for (int i = 0; i < artworks.Length; i++)
            ResetArtwork(i);
    }

    //다음 과제 출제
    void NextTrial()
    {
        int[] targets = targetOrders[block % 2];
        if (trial >= targets.Length)
        {
            EndBlock();
            return;
        }

        target = targets[trial];
        trial++;
        ResetAllArtworks();
        SetGrabbable(true);
        prompt.text = $"{artworks[target].label} 작품을 노란 표시 위에 올려 주세요";

        targetRenderer.material.color = targetColor;
        wrongGrabs = 0;
        for (int i = 0; i < wasSelected.Length; i++) wasSelected[i] = false;
        startTime = Time.realtimeSinceStartup;
        measuring = true;
    }

    //잡은 순간 / 놓은 순간 확인
    void CheckArtworks()
    {
        for (int i = 0; i < artworks.Length; i++)
        {
            bool selected = IsSelected(i);

            //방금 잡았다면 → 다른 작품이면 오류
            if (selected && !wasSelected[i] && i != target)
                wrongGrabs++;

            //방금 놓았다면
            if (!selected && wasSelected[i])
            {
                if (i == target && IsOnTarget())
                {
                    CompleteTrial();
                    return;
                }
                if (i != target) ResetArtwork(i); //잘못 잡은 작품은 원래 자리로
            }

            wasSelected[i] = selected;
        }
    }

    //옮길 작품이 노란 표시 위에 있는지
    bool IsOnTarget()
    {
        Vector3 local = targetPoint.InverseTransformPoint(artworks[target].body.position);
        float horizontal = new Vector2(local.x, local.z).magnitude; //옆으로 떨어진 거리

        return horizontal < targetRadius && local.y > -0.01f && local.y < targetHeight;
    }

    //과제 완료
    void CompleteTrial()
    {
        measuring = false;
        float taskTime = Time.realtimeSinceStartup - startTime;

        //표시 위에 반듯하게 내려놓기
        Transform body = artworks[target].body;
        body.position = targetPoint.position + Vector3.up * (body.localScale.y * 0.5f);
        body.rotation = station.rotation;
        SetGrabbable(false); //쉬는 동안 잡지 못하게
        targetRenderer.material.color = new Color(0.3f, 1f, 0.4f); //완료 표시 (초록)

        WriteLog(taskTime);
        StartCoroutine(RestThenNext());
    }

    //과제 사이 쉬는 시간
    IEnumerator RestThenNext()
    {
        prompt.text = "";
        yield return new WaitForSeconds(restTime);
        NextTrial();
    }

    //블록 종료
    void EndBlock()
    {
        blockRunning = false;
        SetGrabbable(false);
        targetRenderer.material.color = targetColor;

        if (block == order.Length - 1) prompt.text = "종료";
        else prompt.text = "잠시 쉬어요";
    }

    //CSV에 한 줄 저장
    void WriteLog(float taskTime)
    {
        string distance = target < 2 ? "near" : "far";
        string line = $"{participantNumber},{block + 1},{order[block]},{trial},{artworks[target].label},{distance},{taskTime:F3},{wrongGrabs}";
        File.AppendAllText(logPath, line + "\n");
    }
}
