using System.Collections;
using System.IO;
using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public enum MenuPlacement { Palm, Side }

public class HandMenuTrialManager : MonoBehaviour
{
    //참가자 설정
    [SerializeField] private int participantNumber = 1; //기록용 참가자 번호
    [SerializeField] private int trialsPerBlock = 3; //블록당 과제 횟수 (4블록 × 3회 = 조건당 6회)
    [SerializeField] private float restTime = 1f; //과제 사이 쉬는 시간

    //연결할 오브젝트
    [SerializeField] private ContentDisplay display; //전시 화면
    [SerializeField] private TMP_Text prompt; //지시문
    [SerializeField] private Transform head; //CenterEyeAnchor
    [SerializeField] private Transform menu; //Menu_Hand
    [SerializeField] private Hand leftHand; //ComprehensiveInteractorsLeft의 Hand
    [SerializeField] private Hand rightHand; //ComprehensiveInteractorsRight의 Hand

    //메뉴 위치
    [SerializeField] private float palmLift = 0.01f; //손바닥 조건 : 손바닥에서 띄우는 거리
    [SerializeField] private float sideOffset = 0.07f; //손 바깥 조건 : 손 오른쪽으로
    [SerializeField] private float upOffset = 0.05f; //손 바깥 조건 : 손 위로

    //블록 순서 (ABBA : 손바닥 → 손 바깥 → 손 바깥 → 손바닥)
    private MenuPlacement[] order = { MenuPlacement.Palm, MenuPlacement.Side, MenuPlacement.Side, MenuPlacement.Palm };

    //진행 상태
    private int block = -1; //몇 번째 블록인지 (-1 : 시작 전, 0 ~ 3)
    private bool blockRunning; //블록 진행 중인지
    private bool practicing; //연습 중인지 (기록 안 함)
    private bool waitingForAnswer; //버튼 입력을 받는 중인지
    private MenuPlacement placement; //지금 메뉴 위치

    //과제 기록
    private int trial; //현재 과제 번호
    private int targetIndex; //눌러야 하는 작품 번호
    private int wrongPresses; //이번 과제의 잘못 누름 횟수
    private int leftLost; //이번 과제에서 왼손 추적이 끊긴 횟수
    private int rightLost; //이번 과제에서 오른손 추적이 끊긴 횟수
    private bool leftWasTracked; //지난 프레임 왼손 추적 상태
    private bool rightWasTracked; //지난 프레임 오른손 추적 상태
    private float startTime; //지시문이 뜬 시간
    private string logPath; //CSV 저장 위치

    void Start()
    {
        //CSV 파일이 없으면 첫 줄(항목 이름) 만들기
        logPath = Path.Combine(Application.persistentDataPath, "handmenu_log.csv");
        if (!File.Exists(logPath))
            File.AppendAllText(logPath, "participant,block,condition,trial,target,timeSec,wrongPresses,leftLost,rightLost\n");
        Debug.Log("저장 위치 : " + logPath);

        menu.gameObject.SetActive(false);
        prompt.text = "준비";
    }

    void Update()
    {
        if (Keyboard.current != null)
        {
            //1, 2 : 연습 (기록 안 함)
            if (Keyboard.current.digit1Key.wasPressedThisFrame && !blockRunning)
                StartPractice(MenuPlacement.Palm);
            if (Keyboard.current.digit2Key.wasPressedThisFrame && !blockRunning)
                StartPractice(MenuPlacement.Side);

            //Space : 첫 블록 시작
            if (Keyboard.current.spaceKey.wasPressedThisFrame && block == -1)
                BeginBlock(0);

            //N : 블록이 끝난 뒤 다음 블록 시작
            bool hasNextBlock = block >= 0 && block < order.Length - 1;
            if (Keyboard.current.nKey.wasPressedThisFrame && hasNextBlock && !blockRunning)
                BeginBlock(block + 1);
        }

        if (waitingForAnswer)
            CountTrackingLoss();
    }

    //손 위치가 갱신된 다음에 메뉴 옮기기 (떨림 줄이기)
    void LateUpdate()
    {
        if (menu.gameObject.activeSelf)
            PlaceMenu();
    }

    //메뉴를 왼손에 붙이기
    void PlaceMenu()
    {
        //손 추적이 끊기면 마지막 위치에 그대로 둠
        if (!GetPalm(out Vector3 center, out Vector3 normal, out Vector3 fingers))
            return;

        if (placement == MenuPlacement.Palm)
        {
            //손바닥 위에 눕혀서 붙임 (손가락 방향이 메뉴 위쪽)
            menu.position = center + normal * palmLift;
            menu.rotation = Quaternion.LookRotation(-normal, fingers);
        }
        else
        {
            //손 오른쪽 위에 띄우고 얼굴을 바라보게 (3주차 손 UI와 같은 방식)
            Vector3 right = head.right;
            right.y = 0;
            right.Normalize();

            menu.position = center + right * sideOffset + Vector3.up * upOffset;
            menu.rotation = Quaternion.LookRotation(menu.position - head.position);
        }
    }

    //왼손 관절 3개(손목, 검지 뿌리, 새끼 뿌리)로 손바닥 가운데와 방향 구하기
    bool GetPalm(out Vector3 center, out Vector3 normal, out Vector3 fingers)
    {
        center = normal = fingers = Vector3.zero;

        if (!leftHand.GetJointPose(HandJointId.HandWristRoot, out Pose wrist)) return false;
        if (!leftHand.GetJointPose(HandJointId.HandIndex1, out Pose index)) return false;
        if (!leftHand.GetJointPose(HandJointId.HandPinky1, out Pose pinky)) return false;

        Vector3 knuckles = (index.position + pinky.position) / 2; //손가락 뿌리 가운데
        center = Vector3.Lerp(wrist.position, knuckles, 0.6f); //손목과 손가락 뿌리 사이
        fingers = (knuckles - wrist.position).normalized; //손목 → 손가락 방향

        //손바닥 면에 수직인 방향 (두 선분의 외적)
        normal = Vector3.Cross(index.position - wrist.position, pinky.position - wrist.position).normalized;

        //손바닥을 얼굴 쪽으로 들고 있으므로, 얼굴 쪽을 향하는 면을 손바닥으로 봄
        if (Vector3.Dot(normal, head.position - center) < 0)
            normal = -normal;

        return true;
    }

    //손 추적이 끊긴 순간 세기 (추적 중 → 끊김으로 바뀐 순간만)
    void CountTrackingLoss()
    {
        bool left = leftHand.IsTrackedDataValid;
        bool right = rightHand.IsTrackedDataValid;

        if (leftWasTracked && !left) leftLost++;
        if (rightWasTracked && !right) rightLost++;

        leftWasTracked = left;
        rightWasTracked = right;
    }

    //연습 : 3회 (기록 안 함)
    void StartPractice(MenuPlacement practicePlacement)
    {
        StopAllCoroutines();
        practicing = true;
        placement = practicePlacement;
        menu.gameObject.SetActive(true);

        trial = 0;
        NextTrial();
    }

    //블록 시작
    void BeginBlock(int index)
    {
        StopAllCoroutines();
        practicing = false;
        block = index;
        placement = order[block];
        menu.gameObject.SetActive(true);

        trial = 0;
        blockRunning = true;
        NextTrial();
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

        //현재 작품과 다른 번호를 랜덤으로
        do { targetIndex = Random.Range(0, 3); }
        while (targetIndex == display.CurrentIndex);

        int number = targetIndex + 1;
        string particle = (number == 3) ? "으로" : "로"; //1로, 2로, 3으로
        prompt.text = $"작품 {number}{particle} 바꿔주세요";

        wrongPresses = 0;
        leftLost = 0;
        rightLost = 0;
        leftWasTracked = leftHand.IsTrackedDataValid;
        rightWasTracked = rightHand.IsTrackedDataValid;
        startTime = Time.realtimeSinceStartup;
        waitingForAnswer = true;
    }

    //메뉴 버튼 OnClick에 연결 (작품 1 = 0, 작품 2 = 1, 작품 3 = 2)
    public void OnMenuButton(int index)
    {
        if (!waitingForAnswer)
            return;

        //잘못 누름 : 횟수만 세고 화면은 바꾸지 않음
        if (index != targetIndex)
        {
            wrongPresses++;
            return;
        }

        //정답 : 시간 계산 → 화면 전환 → 기록 → 쉬고 다음 과제
        float taskTime = Time.realtimeSinceStartup - startTime;
        display.Show(index);
        if (!practicing) WriteLog(taskTime);
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

    //블록 종료
    void EndBlock()
    {
        waitingForAnswer = false;
        menu.gameObject.SetActive(false);

        if (practicing)
        {
            practicing = false;
            prompt.text = "연습 끝";
            return;
        }

        blockRunning = false;
        if (block == order.Length - 1) prompt.text = "종료";
        else prompt.text = "잠시 쉬어요";
    }

    //CSV에 한 줄 저장
    void WriteLog(float taskTime)
    {
        string line = $"{participantNumber},{block + 1},{placement},{trial},{targetIndex + 1},{taskTime:F3},{wrongPresses},{leftLost},{rightLost}";
        File.AppendAllText(logPath, line + "\n");
    }
}
