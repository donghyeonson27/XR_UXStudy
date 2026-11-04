using System.Collections;
using System.IO;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public enum GrabCondition { Pinch, Palm }

public class GrabTrialManager : MonoBehaviour
{
    //참가자 설정
    [SerializeField] private int participantNumber = 1; //기록용 참가자 번호
    [SerializeField] private int trialsPerBlock = 3; //블록당 과제 횟수 (4블록 × 3회 = 조건당 6회)
    [SerializeField] private float restTime = 1.5f; //과제 사이 쉬는 시간

    //연결할 오브젝트
    [SerializeField] private Transform station; //GrabStation (테이블 전체)
    [SerializeField] private Transform artwork; //옮길 작품 오브제
    [SerializeField] private Renderer artworkRenderer; //오브제 색
    [SerializeField] private HandGrabInteractable grabInteractable; //오브제의 Hand Grab
    [SerializeField] private Transform startPoint; //시작 위치
    [SerializeField] private Transform targetPoint; //목표 위치
    [SerializeField] private Renderer targetRenderer; //목표 표시 색
    [SerializeField] private TMP_Text prompt; //지시문
    [SerializeField] private Transform head; //CenterEyeAnchor

    //테이블 위치
    [SerializeField] private float stationDistance = 0.45f; //머리 앞 거리
    [SerializeField] private float stationDrop = 0.5f; //눈높이보다 낮추는 값 (테이블 높이)

    //성공 판정
    [SerializeField] private float targetRadius = 0.04f; //목표 표시 반지름 (4cm)
    [SerializeField] private float targetHeight = 0.08f; //표시 위 8cm 안에서 놓으면 성공

    //오브제 색 (두 조건 모두 같은 피드백)
    [SerializeField] private Color normalColor = new Color(1f, 0.55f, 0.2f); //기본
    [SerializeField] private Color hoverColor = new Color(1f, 0.8f, 0.4f); //손이 가까이 왔을 때
    [SerializeField] private Color selectColor = new Color(0.4f, 0.8f, 1f); //잡았을 때

    //블록 순서 (ABBA : 핀치 → 그랩 → 그랩 → 핀치)
    private GrabCondition[] order = { GrabCondition.Pinch, GrabCondition.Palm, GrabCondition.Palm, GrabCondition.Pinch };

    //진행 상태
    private int block = -1; //몇 번째 블록인지 (-1 : 시작 전, 0 ~ 3)
    private bool blockRunning; //블록 진행 중인지
    private bool measuring; //과제 측정 중인지

    //과제 기록
    private int trial; //블록 안에서 몇 번째 과제인지
    private int drops; //목표 밖에서 놓친 횟수
    private bool wasGrabbed; //지난 프레임에 잡고 있었는지
    private float startTime; //지시문이 뜬 시간
    private Color targetColor; //목표 표시 원래 색
    private string logPath; //CSV 저장 위치

    void Start()
    {
        //CSV 파일이 없으면 첫 줄(항목 이름) 만들기
        logPath = Path.Combine(Application.persistentDataPath, "grab_log.csv");
        if (!File.Exists(logPath))
            File.AppendAllText(logPath, "participant,block,condition,trial,timeSec,drops\n");
        Debug.Log("저장 위치 : " + logPath);

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
                StartPractice(GrabCondition.Pinch);
            if (Keyboard.current.digit2Key.wasPressedThisFrame && !blockRunning)
                StartPractice(GrabCondition.Palm);

            //Space : 첫 블록 시작
            if (Keyboard.current.spaceKey.wasPressedThisFrame && block == -1)
                BeginBlock(0);

            //N : 블록이 끝난 뒤 다음 블록 시작
            bool hasNextBlock = block >= 0 && block < order.Length - 1;
            if (Keyboard.current.nKey.wasPressedThisFrame && hasNextBlock && !blockRunning)
                BeginBlock(block + 1);
        }

        UpdateArtworkColor();

        if (measuring)
            CheckArtwork();
    }

    //잡을 수 있게 / 없게
    void SetGrabbable(bool canGrab)
    {
        grabInteractable.gameObject.SetActive(canGrab);
    }

    //잡는 방식 정하기 (핀치만 / 손바닥 그랩만)
    void SetCondition(GrabCondition condition)
    {
        if (condition == GrabCondition.Pinch) grabInteractable.InjectSupportedGrabTypes(GrabTypeFlags.Pinch);
        else grabInteractable.InjectSupportedGrabTypes(GrabTypeFlags.Palm);

        SetGrabbable(true);
    }

    //손이 가까이 오거나 잡으면 오브제 색 바꾸기
    void UpdateArtworkColor()
    {
        if (!grabInteractable.gameObject.activeInHierarchy)
        {
            artworkRenderer.material.color = normalColor;
            return;
        }

        if (grabInteractable.State == InteractableState.Select) artworkRenderer.material.color = selectColor;
        else if (grabInteractable.State == InteractableState.Hover) artworkRenderer.material.color = hoverColor;
        else artworkRenderer.material.color = normalColor;
    }

    //연습 : 자유롭게 옮겨보기 (기록 안 함)
    void StartPractice(GrabCondition condition)
    {
        PlaceStation();
        ResetArtwork();
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

    //테이블을 머리 위치 기준으로 배치
    void PlaceStation()
    {
        Vector3 forward = head.forward;
        forward.y = 0; //고개를 숙이고 있어도 수평 방향 기준으로
        forward.Normalize();

        station.position = head.position + forward * stationDistance + Vector3.down * stationDrop;
        station.rotation = Quaternion.LookRotation(forward); //테이블이 기울지 않게 수평으로
    }

    //오브제를 시작 위치에 올려두기
    void ResetArtwork()
    {
        artwork.position = startPoint.position + Vector3.up * (artwork.localScale.y * 0.5f);
        artwork.rotation = startPoint.rotation;
    }

    //다음 과제 출제
    void NextTrial()
    {
        if (trial >= trialsPerBlock)
        {
            EndBlock();
            return;
        }

        trial++;
        ResetArtwork();
        SetGrabbable(true);
        prompt.text = "작품을 노란 표시 위에 올려 주세요";

        targetRenderer.material.color = targetColor;
        drops = 0;
        wasGrabbed = false;
        startTime = Time.realtimeSinceStartup;
        measuring = true;
    }

    //오브제를 놓은 순간 목표 위치인지 확인
    void CheckArtwork()
    {
        bool isGrabbed = grabInteractable.State == InteractableState.Select;

        //방금 놓았다면
        if (wasGrabbed && !isGrabbed)
        {
            if (IsOnTarget()) CompleteTrial();
            else drops++; //목표 밖에서 놓침
        }

        wasGrabbed = isGrabbed;
    }

    //오브제가 목표 표시 위에 있는지
    bool IsOnTarget()
    {
        //목표 위치 기준으로 오브제가 어디에 있는지 (로컬 좌표)
        Vector3 local = targetPoint.InverseTransformPoint(artwork.position);
        float horizontal = new Vector2(local.x, local.z).magnitude; //옆으로 떨어진 거리

        return horizontal < targetRadius && local.y > -0.01f && local.y < targetHeight;
    }

    //과제 완료
    void CompleteTrial()
    {
        measuring = false;
        float taskTime = Time.realtimeSinceStartup - startTime;

        //표시 위에 반듯하게 내려놓기
        artwork.position = targetPoint.position + Vector3.up * (artwork.localScale.y * 0.5f);
        artwork.rotation = targetPoint.rotation;
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
        string line = $"{participantNumber},{block + 1},{order[block]},{trial},{taskTime:F3},{drops}";
        File.AppendAllText(logPath, line + "\n");
    }
}
