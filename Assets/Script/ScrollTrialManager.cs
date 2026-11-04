using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum ScrollCondition { Microgesture, PokeSwipe }

public class ScrollTrialManager : MonoBehaviour
{
    //참가자 설정
    [SerializeField] private int participantNumber = 1; //기록용 참가자 번호
    [SerializeField] private int trialsPerBlock = 3; //블록당 과제 횟수 (4블록 × 3회 = 조건당 6회)
    [SerializeField] private float restTime = 1.5f; //과제 사이 쉬는 시간

    //연결할 오브젝트
    [SerializeField] private Transform panel; //InfoPanel (패널 전체)
    [SerializeField] private RectTransform content; //스크롤되는 글 묶음
    [SerializeField] private RectTransform[] sections; //문단들
    [SerializeField] private string[] sectionTitles; //문단 제목 (찾을 단어)
    [SerializeField] private RectTransform zone; //가운데 표시 영역
    [SerializeField] private Image zoneImage; //표시 영역 색
    [SerializeField] private TMP_Text prompt; //지시문
    [SerializeField] private Transform head; //CenterEyeAnchor
    [SerializeField] private GameObject pokeInteraction; //Poke 상호작용 오브젝트
    [SerializeField] private MicrogestureScroll microgestureScroll; //마이크로 제스처 스크롤

    //패널 위치
    [SerializeField] private float panelDistance = 0.45f; //머리 앞 거리
    [SerializeField] private float panelDrop = 0.15f; //눈높이보다 낮추는 값

    //완료 판정
    [SerializeField] private float zoneRange = 60f; //가운데에서 이 거리(px) 안이면 영역 안
    [SerializeField] private float stillTime = 0.5f; //영역 안에서 이 시간 동안 멈추면 완료

    //블록 순서 (ABBA : 마이크로 제스처 → Poke → Poke → 마이크로 제스처)
    private ScrollCondition[] order = { ScrollCondition.Microgesture, ScrollCondition.PokeSwipe, ScrollCondition.PokeSwipe, ScrollCondition.Microgesture };

    //진행 상태
    private int block = -1; //몇 번째 블록인지 (-1 : 시작 전, 0 ~ 3)
    private bool blockRunning; //블록 진행 중인지
    private bool measuring; //과제 측정 중인지

    //과제 기록
    private int trial; //블록 안에서 몇 번째 과제인지
    private int targetIndex; //찾아야 하는 문단 번호
    private int lastTargetIndex = 0; //직전 과제의 문단 번호 (처음에는 맨 위)
    private int overshoots; //목표를 지나친 횟수
    private bool wasInZone; //지난 프레임에 영역 안이었는지
    private float stillTimer; //영역 안에서 멈춰 있던 시간
    private float lastContentY; //지난 프레임의 글 위치
    private float startTime; //지시문이 뜬 시간
    private Color zoneColor; //표시 영역 원래 색
    private string logPath; //CSV 저장 위치

    void Start()
    {
        //CSV 파일이 없으면 첫 줄(항목 이름) 만들기
        logPath = Path.Combine(Application.persistentDataPath, "scroll_log.csv");
        if (!File.Exists(logPath))
            File.AppendAllText(logPath, "participant,block,condition,trial,target,timeSec,overshoots\n");
        Debug.Log("저장 위치 : " + logPath);

        zoneColor = zoneImage.color;
        SetInput(false, false);
        prompt.text = "준비";
    }

    void Update()
    {
        if (Keyboard.current != null)
        {
            //1, 2 : 연습 (기록 안 함)
            if (Keyboard.current.digit1Key.wasPressedThisFrame && !blockRunning)
                StartPractice(ScrollCondition.Microgesture);
            if (Keyboard.current.digit2Key.wasPressedThisFrame && !blockRunning)
                StartPractice(ScrollCondition.PokeSwipe);

            //Space : 첫 블록 시작
            if (Keyboard.current.spaceKey.wasPressedThisFrame && block == -1)
                BeginBlock(0);

            //N : 블록이 끝난 뒤 다음 블록 시작
            bool hasNextBlock = block >= 0 && block < order.Length - 1;
            if (Keyboard.current.nKey.wasPressedThisFrame && hasNextBlock && !blockRunning)
                BeginBlock(block + 1);
        }

        if (measuring)
            CheckTarget();
    }

    //스크롤 방식 켜고 끄기
    void SetInput(bool usePoke, bool useMicrogesture)
    {
        pokeInteraction.SetActive(usePoke);
        microgestureScroll.enabled = useMicrogesture;
    }

    void SetCondition(ScrollCondition condition)
    {
        SetInput(condition == ScrollCondition.PokeSwipe, condition == ScrollCondition.Microgesture);
    }

    //연습 : 자유롭게 스크롤 (기록 안 함)
    void StartPractice(ScrollCondition condition)
    {
        PlacePanel();
        SetCondition(condition);
        prompt.text = "연습 : 자유롭게 스크롤해 보세요";
    }

    //블록 시작
    void BeginBlock(int index)
    {
        block = index;
        PlacePanel();
        SetCondition(order[block]);

        trial = 0;
        blockRunning = true;
        NextTrial();
    }

    //패널을 머리 위치 기준으로 배치
    void PlacePanel()
    {
        Vector3 forward = head.forward;
        forward.y = 0; //고개를 숙이고 있어도 수평 방향 기준으로
        forward.Normalize();

        panel.position = head.position + forward * panelDistance + Vector3.down * panelDrop;
        panel.rotation = Quaternion.LookRotation(panel.position - head.position); //머리를 바라보게
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

        //직전 문단에서 4칸 이상 떨어진 문단을 랜덤으로 (스크롤 거리를 비슷하게)
        do { targetIndex = Random.Range(0, sections.Length); }
        while (Mathf.Abs(targetIndex - lastTargetIndex) < 4);

        prompt.text = $"[{sectionTitles[targetIndex]}] 찾아서 가운데 칸에 맞춰 주세요";

        zoneImage.color = zoneColor;
        overshoots = 0;
        wasInZone = false;
        stillTimer = 0f;
        lastContentY = content.anchoredPosition.y;
        startTime = Time.realtimeSinceStartup;
        measuring = true;
    }

    //목표 문단이 가운데 영역에 들어와 멈췄는지 확인
    void CheckTarget()
    {
        //목표 문단이 가운데 영역에서 위아래로 얼마나 떨어져 있는지
        float offset = zone.InverseTransformPoint(sections[targetIndex].position).y;
        bool inZone = Mathf.Abs(offset) < zoneRange;

        //영역 안에 있다가 밖으로 나가면 = 지나침
        if (wasInZone && !inZone)
            overshoots++;
        wasInZone = inZone;

        //글이 멈춰 있는지 (한 프레임 동안 움직인 거리)
        float moved = Mathf.Abs(content.anchoredPosition.y - lastContentY);
        lastContentY = content.anchoredPosition.y;

        if (inZone && moved < 0.5f) stillTimer += Time.deltaTime;
        else stillTimer = 0f;

        if (stillTimer >= stillTime)
            CompleteTrial();
    }

    //과제 완료
    void CompleteTrial()
    {
        measuring = false;

        //멈춰 있던 시간(stillTime)은 빼고 '들어와 멈춘 순간'까지의 시간으로 기록
        float taskTime = Time.realtimeSinceStartup - startTime - stillTime;
        lastTargetIndex = targetIndex;

        zoneImage.color = new Color(0.3f, 1f, 0.4f, 0.35f); //완료 표시 (초록)
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
        SetInput(false, false); //쉬는 동안 스크롤 막기
        zoneImage.color = zoneColor;

        if (block == order.Length - 1) prompt.text = "종료";
        else prompt.text = "잠시 쉬어요";
    }

    //CSV에 한 줄 저장
    void WriteLog(float taskTime)
    {
        string line = $"{participantNumber},{block + 1},{order[block]},{trial},{sectionTitles[targetIndex]},{taskTime:F3},{overshoots}";
        File.AppendAllText(logPath, line + "\n");
    }
}
