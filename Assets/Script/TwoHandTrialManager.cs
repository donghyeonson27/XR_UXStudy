using System.Collections;
using System.IO;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public enum TwoHandCondition { Free, Constrained }

public class TwoHandTrialManager : MonoBehaviour
{
    //참가자 설정
    [SerializeField] private int participantNumber = 1; //기록용 참가자 번호
    [SerializeField] private int trialsPerBlock = 3; //블록당 과제 횟수 (4블록 × 3회 = 조건당 6회)
    [SerializeField] private float restTime = 1.5f; //과제 사이 쉬는 시간

    //연결할 오브젝트
    [SerializeField] private Transform station; //GrabStation (테이블 전체)
    [SerializeField] private Transform artwork; //맞출 작품 오브제
    [SerializeField] private Renderer artworkRenderer; //오브제 색
    [SerializeField] private Grabbable grabbable; //오브제의 Grabbable
    [SerializeField] private HandGrabInteractable grabInteractable; //오브제의 Hand Grab
    [SerializeField] private OneGrabTranslateTransformer moveTransformer; //한 손 : 이동만 (두 조건 같음)
    [SerializeField] private TwoGrabFreeTransformer freeTransformer; //자유 : 모든 축으로 회전
    [SerializeField] private TwoGrabPlaneTransformer planeTransformer; //제약 : Y축으로만 회전
    [SerializeField] private Transform guide; //반투명 가이드 (목표 크기·방향)
    [SerializeField] private Renderer guideRenderer; //가이드 색
    [SerializeField] private Transform startPoint; //오브제와 가이드가 놓이는 위치
    [SerializeField] private TMP_Text prompt; //지시문
    [SerializeField] private Transform head; //CenterEyeAnchor

    //테이블 위치
    [SerializeField] private float stationDistance = 0.45f; //머리 앞 거리
    [SerializeField] private float stationDrop = 0.5f; //눈높이보다 낮추는 값 (테이블 높이)

    //가이드 (목표)
    [SerializeField] private float guideScale = 2f; //시작 크기의 2배
    [SerializeField] private float guideAngle = 90f; //Y축으로 90도

    //크기 제한 (두 조건 같음)
    [SerializeField] private float minScale = 0.5f; //시작 크기의 0.5배 (10cm → 5cm)
    [SerializeField] private float maxScale = 4f; //시작 크기의 4배 (10cm → 40cm)

    //성공 판정
    [SerializeField] private float scaleTolerance = 0.1f; //크기 ±10%
    [SerializeField] private float angleTolerance = 15f; //방향 차이 15도 이하

    //오브제 색 (두 조건 모두 같은 피드백)
    [SerializeField] private Color normalColor = new Color(1f, 0.55f, 0.2f); //기본
    [SerializeField] private Color hoverColor = new Color(1f, 0.8f, 0.4f); //손이 가까이 왔을 때
    [SerializeField] private Color selectColor = new Color(0.4f, 0.8f, 1f); //잡았을 때

    //블록 순서 (ABBA : 자유 → 제약 → 제약 → 자유)
    private TwoHandCondition[] order = { TwoHandCondition.Free, TwoHandCondition.Constrained, TwoHandCondition.Constrained, TwoHandCondition.Free };

    //진행 상태
    private int block = -1; //몇 번째 블록인지 (-1 : 시작 전, 0 ~ 3)
    private bool blockRunning; //블록 진행 중인지
    private bool measuring; //과제 측정 중인지

    //과제 기록
    private int trial; //블록 안에서 몇 번째 과제인지
    private int regrabs; //범위 밖에서 놓아 다시 잡은 횟수
    private bool wasGrabbed; //지난 프레임에 잡고 있었는지
    private float startTime; //지시문이 뜬 시간
    private Vector3 startScale; //오브제 시작 크기
    private Color guideColor; //가이드 원래 색
    private string logPath; //CSV 저장 위치

    void Start()
    {
        //CSV 파일이 없으면 첫 줄(항목 이름) 만들기
        logPath = Path.Combine(Application.persistentDataPath, "twohand_log.csv");
        if (!File.Exists(logPath))
            File.AppendAllText(logPath, "participant,block,condition,trial,timeSec,regrabs,tiltDeg\n");
        Debug.Log("저장 위치 : " + logPath);

        startScale = artwork.localScale;

        //한 손으로 잡으면 이동만 → 크기와 회전은 두 손으로만 바뀜
        moveTransformer.Initialize(grabbable);
        grabbable.InjectOptionalOneGrabTransformer(moveTransformer);

        guideColor = guideRenderer.material.color;
        SetScaleLimits();
        SetGrabbable(false);
        guide.gameObject.SetActive(false);
        prompt.text = "준비";
    }

    void Update()
    {
        if (Keyboard.current != null)
        {
            //1, 2 : 연습 (기록 안 함)
            if (Keyboard.current.digit1Key.wasPressedThisFrame && !blockRunning)
                StartPractice(TwoHandCondition.Free);
            if (Keyboard.current.digit2Key.wasPressedThisFrame && !blockRunning)
                StartPractice(TwoHandCondition.Constrained);

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

    //두 조건에 같은 크기 제한 넣기
    void SetScaleLimits()
    {
        //Transformer는 크기를 localScale.x 기준으로 제한함
        float min = startScale.x * minScale;
        float max = startScale.x * maxScale;

        var freeLimit = new TwoGrabFreeTransformer.TwoGrabFreeConstraints();
        freeLimit.MinScale = new FloatConstraint { Constrain = true, Value = min };
        freeLimit.MaxScale = new FloatConstraint { Constrain = true, Value = max };
        freeTransformer.InjectOptionalConstraints(freeLimit);

        var planeLimit = new TwoGrabPlaneTransformer.TwoGrabPlaneConstraints();
        planeLimit.MinScale = new FloatConstraint { Constrain = true, Value = min };
        planeLimit.MaxScale = new FloatConstraint { Constrain = true, Value = max };
        planeLimit.MinY = new FloatConstraint(); //높이 제한은 쓰지 않음
        planeLimit.MaxY = new FloatConstraint();
        planeTransformer.InjectOptionalConstraints(planeLimit);

        //회전 기준 평면 = 테이블 (테이블의 위쪽 방향이 회전축)
        planeTransformer.InjectOptionalPlaneTransform(station);
    }

    //잡을 수 있게 / 없게
    void SetGrabbable(bool canGrab)
    {
        grabInteractable.gameObject.SetActive(canGrab);
    }

    //두 손 조작 방식 정하기 (자유 / 제약)
    void SetCondition(TwoHandCondition condition)
    {
        ITransformer transformer;
        if (condition == TwoHandCondition.Free) transformer = freeTransformer;
        else transformer = planeTransformer;

        transformer.Initialize(grabbable);
        grabbable.InjectOptionalTwoGrabTransformer(transformer);

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

    //연습 : 자유롭게 맞춰보기 (기록 안 함)
    void StartPractice(TwoHandCondition condition)
    {
        PlaceStation();
        ResetArtwork();
        PlaceGuide();
        SetCondition(condition);
        prompt.text = "연습 : 자유롭게 맞춰 보세요";
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

    //오브제를 시작 크기·방향으로 올려두기
    void ResetArtwork()
    {
        artwork.localScale = startScale;
        artwork.position = startPoint.position + Vector3.up * (startScale.y * 0.5f);
        artwork.rotation = startPoint.rotation;
    }

    //가이드를 목표 크기·방향으로 놓기 (오브제와 같은 자리)
    void PlaceGuide()
    {
        guide.gameObject.SetActive(true);
        guide.localScale = startScale * guideScale;
        guide.position = startPoint.position + Vector3.up * (guide.localScale.y * 0.5f);
        guide.rotation = startPoint.rotation * Quaternion.Euler(0, guideAngle, 0);
        guideRenderer.material.color = guideColor;
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
        PlaceGuide();
        SetGrabbable(true);
        prompt.text = "작품을 반투명한 모습과 같은 크기, 같은 방향으로 맞춰 주세요";

        regrabs = 0;
        wasGrabbed = false;
        startTime = Time.realtimeSinceStartup;
        measuring = true;
    }

    //두 손을 모두 놓은 순간 맞았는지 확인
    void CheckArtwork()
    {
        bool isGrabbed = grabInteractable.State == InteractableState.Select;

        //방금 놓았다면
        if (wasGrabbed && !isGrabbed)
        {
            if (IsMatched()) CompleteTrial();
            else regrabs++; //범위 밖에서 놓음
        }

        wasGrabbed = isGrabbed;
    }

    //크기와 방향이 가이드와 맞는지 (위치는 보지 않음)
    bool IsMatched()
    {
        float scaleRatio = artwork.localScale.x / guide.localScale.x; //1이면 같은 크기
        float angle = Quaternion.Angle(artwork.rotation, guide.rotation); //두 방향 사이 각도

        return Mathf.Abs(scaleRatio - 1f) <= scaleTolerance && angle <= angleTolerance;
    }

    //과제 완료
    void CompleteTrial()
    {
        measuring = false;
        float taskTime = Time.realtimeSinceStartup - startTime;
        float tilt = Vector3.Angle(artwork.up, station.up); //테이블 기준 기울어진 각도

        SetGrabbable(false); //쉬는 동안 잡지 못하게
        Color done = new Color(0.3f, 1f, 0.4f, guideColor.a); //완료 표시 (초록, 투명도 유지)
        guideRenderer.material.color = done;

        WriteLog(taskTime, tilt);
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
        guide.gameObject.SetActive(false);

        if (block == order.Length - 1) prompt.text = "종료";
        else prompt.text = "잠시 쉬어요";
    }

    //CSV에 한 줄 저장
    void WriteLog(float taskTime, float tilt)
    {
        string line = $"{participantNumber},{block + 1},{order[block]},{trial},{taskTime:F3},{regrabs},{tilt:F1}";
        File.AppendAllText(logPath, line + "\n");
    }
}
