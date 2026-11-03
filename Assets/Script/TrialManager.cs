using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public enum UICondition { Floating, Hand }

public class TrialManager : MonoBehaviour
{
    //참가자 설정
    [SerializeField] private int participantNumber = 1; //홀수 : 플로팅 먼저, 짝수 : 손 UI 먼저
    [SerializeField] private int trialCount = 9; //조건당 과제 횟수
    [SerializeField] private float restTime = 1f; //과제 사이 쉬는 시간

    //연결할 오브젝트
    [SerializeField] private ContentDisplay display;
    [SerializeField] private TMP_Text prompt; //지시문
    [SerializeField] private Transform head; //CenterEyeAnchor
    [SerializeField] private Transform floatingMenu; //Menu_Floating
    [SerializeField] private GameObject handMenu; //Menu_Hand

    //플로팅 UI 위치
    [SerializeField] private float floatingDistance = 0.4f; //머리 앞 거리
    [SerializeField] private float floatingDrop = 0.2f; //눈높이보다 낮추는 값

    //진행 상태
    private UICondition[] order; //조건 순서
    private int conditionStep = -1; //몇 번째 조건인지 (-1 : 시작 전, 0, 1)
    private bool conditionRunning; //조건 진행 중인지
    private bool waitingForAnswer; //버튼 입력을 받는 중인지

    //과제 기록
    private int trial; //현재 과제 번호
    private int targetIndex; //눌러야 하는 작품 번호
    private int errors; //이번 과제의 오답 횟수
    private float startTime; //지시문이 뜬 시간
    private string logPath; //CSV 저장 위치

    void Start()
    {
        //참가자 번호로 조건 순서 정하기
        if (participantNumber % 2 == 1) order = new[] { UICondition.Floating, UICondition.Hand };
        else order = new[] { UICondition.Hand, UICondition.Floating };

        //CSV 파일이 없으면 첫 줄(항목 이름) 만들기
        logPath = Path.Combine(Application.persistentDataPath, "select_log.csv");
        if (!File.Exists(logPath))
            File.AppendAllText(logPath, "participant,order,condition,trial,target,timeSec,errors\n");
        Debug.Log("저장 위치 : " + logPath);

        floatingMenu.gameObject.SetActive(false);
        handMenu.SetActive(false);
        prompt.text = "준비";
    }

    void Update()
    {
        if (Keyboard.current == null)
            return;

        //Space : 첫 조건 시작
        if (Keyboard.current.spaceKey.wasPressedThisFrame && conditionStep == -1)
            BeginCondition(0);

        //N : 첫 조건이 끝난 뒤 다음 조건 시작
        if (Keyboard.current.nKey.wasPressedThisFrame && conditionStep == 0 && !conditionRunning)
            BeginCondition(1);
    }

    //조건 시작
    void BeginCondition(int step)
    {
        conditionStep = step;
        UICondition condition = order[step];

        //현재 조건 메뉴만 켜기
        floatingMenu.gameObject.SetActive(condition == UICondition.Floating);
        handMenu.SetActive(condition == UICondition.Hand);

        if (condition == UICondition.Floating)
            PlaceFloatingMenu();

        trial = 0;
        conditionRunning = true;
        NextTrial();
    }

    //플로팅 UI를 머리 위치 기준으로 배치
    void PlaceFloatingMenu()
    {
        Vector3 forward = head.forward;
        forward.y = 0; //고개를 숙이고 있어도 수평 방향 기준으로
        forward.Normalize();

        floatingMenu.position = head.position + forward * floatingDistance + Vector3.down * floatingDrop;
        floatingMenu.rotation = Quaternion.LookRotation(floatingMenu.position - head.position); //머리를 바라보게
    }

    //다음 과제 출제
    void NextTrial()
    {
        if (trial >= trialCount)
        {
            EndCondition();
            return;
        }

        trial++;

        //현재 작품과 다른 번호를 랜덤으로
        do { targetIndex = Random.Range(0, 3); }
        while (targetIndex == display.CurrentIndex);

        int number = targetIndex + 1;
        string particle = (number == 3) ? "으로" : "로"; //1로, 2로, 3으로
        prompt.text = $"작품 {number}{particle} 바꿔주세요";

        errors = 0;
        startTime = Time.realtimeSinceStartup;
        waitingForAnswer = true;
    }

    //메뉴 버튼 OnClick에 연결 (작품 1 = 0, 작품 2 = 1, 작품 3 = 2)
    public void OnMenuButton(int index)
    {
        if (!waitingForAnswer)
            return;

        //오답 : 횟수만 세고 화면은 바꾸지 않음
        if (index != targetIndex)
        {
            errors++;
            return;
        }

        //정답 : 시간 계산 → 화면 전환 → 기록 → 쉬고 다음 과제
        float taskTime = Time.realtimeSinceStartup - startTime;
        display.Show(index);
        WriteLog(taskTime);
        StartCoroutine(RestThenNext());
    }

    //과제 사이 쉬는 시간
    IEnumerator RestThenNext()
    {
        waitingForAnswer = false;
        prompt.text = "";
        yield return new WaitForSeconds(restTime);
        NextTrial();
    }

    //조건 종료
    void EndCondition()
    {
        conditionRunning = false;
        waitingForAnswer = false;
        floatingMenu.gameObject.SetActive(false);
        handMenu.SetActive(false);

        if (conditionStep == 0) prompt.text = "조건 종료";
        else prompt.text = "종료";
    }

    //CSV에 한 줄 저장
    void WriteLog(float taskTime)
    {
        string orderText = (order[0] == UICondition.Floating) ? "FH" : "HF"; //F : 플로팅, H : 손 UI
        string line = $"{participantNumber},{orderText},{order[conditionStep]},{trial},{targetIndex + 1},{taskTime:F3},{errors}";
        File.AppendAllText(logPath, line + "\n");
    }
}
