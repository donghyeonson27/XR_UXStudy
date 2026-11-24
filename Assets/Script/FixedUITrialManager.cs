using System.Collections;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PanelCondition { WorldLocked, TagAlong }

public class FixedUITrialManager : MonoBehaviour
{
    //참가자 설정
    [SerializeField] private int participantNumber = 1; //기록용 참가자 번호
    [SerializeField] private float restTime = 1.5f; //과제 사이 쉬는 시간

    //연결할 오브젝트
    [SerializeField] private Transform head; //CenterEyeAnchor
    [SerializeField] private RectTransform panel; //안내 패널 (PromptCanvas)
    [SerializeField] private TMP_Text panelText; //안내 패널 글자
    [SerializeField] private GameObject station; //5주차 테이블 (이번엔 끔)
    [SerializeField] private Material artworkMaterial; //작품 머티리얼 (색은 코드에서)

    //거리 (머리 기준 수평 거리)
    [SerializeField] private float panelDistance = 1.2f; //안내 패널 거리
    [SerializeField] private float panelDrop = 0.15f; //패널이 눈높이보다 낮은 정도
    [SerializeField] private float artworkDistance = 3.6f; //작품 거리
    [SerializeField] private float artworkSize = 0.3f; //작품 크기
    [SerializeField] private float[] artworkAngles = { -75f, -25f, 25f, 75f }; //작품 좌우 각도

    //태그얼롱
    [SerializeField] private float followAngle = 30f; //시야 중심에서 이만큼 벗어나면 따라오기 시작
    [SerializeField] private float followSpeed = 3f; //따라오는 속도 (클수록 빠름)

    //바라보기 판정
    [SerializeField] private float lookAngle = 8f; //작품 방향과 시선이 이 각도 안이면 바라보는 중
    [SerializeField] private float dwellTime = 1f; //이만큼 계속 바라보면 확인

    //작품 4개 (이름, 색)
    private string[] labels = { "빨강", "파랑", "노랑", "초록" };
    private Color[] colors =
    {
        new Color(0.9f, 0.2f, 0.2f),
        new Color(0.2f, 0.45f, 0.95f),
        new Color(0.95f, 0.85f, 0.2f),
        new Color(0.25f, 0.8f, 0.35f)
    };

    //블록 순서 (ABBA : 공간 고정 → 태그얼롱 → 태그얼롱 → 공간 고정)
    private PanelCondition[] order = { PanelCondition.WorldLocked, PanelCondition.TagAlong, PanelCondition.TagAlong, PanelCondition.WorldLocked };

    //바라볼 순서 (과제 하나 = 작품 3개)
    //0, 1블록은 앞 3개, 2, 3블록은 뒤 3개 → 두 조건 모두 같은 6개 순서를 한 번씩
    private int[][] sequences =
    {
        new[] { 0, 3, 1 }, new[] { 2, 0, 3 }, new[] { 1, 2, 0 },
        new[] { 3, 1, 2 }, new[] { 0, 2, 1 }, new[] { 3, 0, 2 }
    };

    //진행 상태
    private int block = -1; //몇 번째 블록인지 (-1 : 시작 전, 0 ~ 3)
    private bool blockRunning; //블록 진행 중인지
    private bool measuring; //과제 측정 중인지
    private PanelCondition condition; //지금 패널 방식
    private bool following; //태그얼롱 패널이 따라오는 중인지

    //과제 기록
    private int trial; //블록 안에서 몇 번째 과제인지
    private int[] sequence; //이번 과제의 바라볼 순서
    private int step; //순서 중 몇 번째를 찾는 중인지
    private float dwell; //지금 작품을 바라본 시간
    private float startTime; //지시문이 뜬 시간
    private float headTurn; //머리를 돌린 총 각도
    private Quaternion lastHeadRotation; //지난 프레임 머리 방향
    private Transform[] artworks = new Transform[4]; //작품
    private Renderer[] artworkRenderers = new Renderer[4]; //작품 색
    private string logPath; //CSV 저장 위치

    void Start()
    {
        //CSV 파일이 없으면 첫 줄(항목 이름) 만들기
        logPath = Path.Combine(Application.persistentDataPath, "fixedui_log.csv");
        if (!File.Exists(logPath))
            File.AppendAllText(logPath, "participant,block,condition,trial,sequence,timeSec,headTurnDeg\n");
        Debug.Log("저장 위치 : " + logPath);

        //패널을 테이블에서 떼어내고, 테이블은 끔
        panel.SetParent(null, true);
        panel.localScale = Vector3.one * 0.001f;
        panel.sizeDelta = new Vector2(700, 200); //두 줄이 들어가게
        station.SetActive(false);

        CreateArtworks();
        panelText.text = "준비";
        PlacePanel();
    }

    void Update()
    {
        if (Keyboard.current != null)
        {
            //1, 2 : 연습 (기록 안 함)
            if (Keyboard.current.digit1Key.wasPressedThisFrame && !blockRunning)
                StartPractice(PanelCondition.WorldLocked);
            if (Keyboard.current.digit2Key.wasPressedThisFrame && !blockRunning)
                StartPractice(PanelCondition.TagAlong);

            //Space : 첫 블록 시작
            if (Keyboard.current.spaceKey.wasPressedThisFrame && block == -1)
                BeginBlock(0);

            //N : 블록이 끝난 뒤 다음 블록 시작
            bool hasNextBlock = block >= 0 && block < order.Length - 1;
            if (Keyboard.current.nKey.wasPressedThisFrame && hasNextBlock && !blockRunning)
                BeginBlock(block + 1);
        }

        if (condition == PanelCondition.TagAlong)
            FollowHead();

        if (measuring)
        {
            //머리를 돌린 각도 더하기
            headTurn += Quaternion.Angle(lastHeadRotation, head.rotation);
            lastHeadRotation = head.rotation;

            CheckLook();
        }
    }

    //작품 4개 만들기 (머리 둘레 3.6m에 좌우로 흩어서)
    void CreateArtworks()
    {
        for (int i = 0; i < 4; i++)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Artwork_" + labels[i];
            cube.transform.localScale = Vector3.one * artworkSize;
            Destroy(cube.GetComponent<Collider>()); //부딪힐 일 없음

            artworkRenderers[i] = cube.GetComponent<Renderer>();
            artworkRenderers[i].material = artworkMaterial;
            artworkRenderers[i].material.color = colors[i];
            artworks[i] = cube.transform;
        }
    }

    //작품을 머리 기준으로 다시 놓기
    void PlaceArtworks()
    {
        Vector3 forward = FlatForward();
        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = Quaternion.Euler(0, artworkAngles[i], 0) * forward;
            Vector3 pos = head.position + dir * artworkDistance;
            pos.y = head.position.y; //눈높이
            artworks[i].position = pos;
            artworks[i].rotation = Quaternion.LookRotation(dir);
            artworkRenderers[i].material.color = colors[i];
        }
    }

    //패널을 머리 정면에 놓기
    void PlacePanel()
    {
        panel.position = PanelTarget();
        FacePanel();
    }

    //패널이 있어야 할 자리 (머리 정면 1.2m, 눈높이보다 조금 아래)
    Vector3 PanelTarget()
    {
        return head.position + FlatForward() * panelDistance + Vector3.down * panelDrop;
    }

    //패널이 머리를 바라보게 (글자가 읽히는 방향)
    void FacePanel()
    {
        Vector3 toPanel = panel.position - head.position;
        toPanel.y = 0;
        panel.rotation = Quaternion.LookRotation(toPanel);
    }

    //태그얼롱 : 시야에서 많이 벗어나면 천천히 따라옴
    void FollowHead()
    {
        Vector3 toPanel = panel.position - head.position;
        toPanel.y = 0;
        float angle = Vector3.Angle(FlatForward(), toPanel);

        if (angle > followAngle) following = true; //많이 벗어나면 따라가기 시작
        if (following)
        {
            panel.position = Vector3.Lerp(panel.position, PanelTarget(), followSpeed * Time.deltaTime);
            if (angle < 2f) following = false; //거의 정면에 오면 멈춤
        }
        FacePanel();
    }

    //고개를 숙여도 수평 방향 기준으로
    Vector3 FlatForward()
    {
        Vector3 forward = head.forward;
        forward.y = 0;
        return forward.normalized;
    }

    //연습 : 자유롭게 둘러보기 (기록 안 함)
    void StartPractice(PanelCondition practiceCondition)
    {
        condition = practiceCondition;
        PlaceArtworks();
        PlacePanel();
        sequence = sequences[0];
        step = 0;
        dwell = 0;
        ShowSequence();
        measuring = false;
        StopAllCoroutines();
        StartCoroutine(PracticeLook());
    }

    //연습 중에도 바라보기 확인 (기록은 안 함)
    IEnumerator PracticeLook()
    {
        while (!blockRunning && step < sequence.Length)
        {
            CheckLook();
            yield return null;
        }
        if (!blockRunning) panelText.text = "연습 끝";
    }

    //블록 시작
    void BeginBlock(int index)
    {
        StopAllCoroutines();
        block = index;
        condition = order[block];
        PlaceArtworks();

        trial = 0;
        blockRunning = true;
        NextTrial();
    }

    //다음 과제 출제
    void NextTrial()
    {
        if (trial >= 3)
        {
            EndBlock();
            return;
        }

        int setStart = block < 2 ? 0 : 3; //0, 1블록은 앞 3개, 2, 3블록은 뒤 3개
        sequence = sequences[setStart + trial];
        trial++;

        PlacePanel(); //패널은 과제마다 정면에서 시작
        following = false;
        for (int i = 0; i < 4; i++) artworkRenderers[i].material.color = colors[i];

        step = 0;
        dwell = 0;
        ShowSequence();

        headTurn = 0;
        lastHeadRotation = head.rotation;
        startTime = Time.realtimeSinceStartup;
        measuring = true;
    }

    //패널에 바라볼 순서 보여주기 (끝난 것은 흐리게)
    void ShowSequence()
    {
        string text = "순서대로 바라보세요\n";
        for (int i = 0; i < sequence.Length; i++)
        {
            string label = labels[sequence[i]];
            if (i < step) label = $"<color=#888888>{label}</color>"; //이미 찾은 작품
            text += label;
            if (i < sequence.Length - 1) text += "  →  ";
        }
        panelText.text = text;
    }

    //지금 찾을 작품을 바라보고 있는지 확인
    void CheckLook()
    {
        int target = sequence[step];
        Vector3 toArtwork = artworks[target].position - head.position;
        bool looking = Vector3.Angle(head.forward, toArtwork) < lookAngle;

        if (looking) dwell += Time.deltaTime;
        else dwell = 0;

        //바라보는 동안 점점 밝아짐 (두 조건 같은 피드백)
        float t = Mathf.Clamp01(dwell / dwellTime) * 0.6f;
        artworkRenderers[target].material.color = Color.Lerp(colors[target], Color.white, t);

        if (dwell >= dwellTime)
        {
            artworkRenderers[target].material.color = colors[target];
            step++;
            dwell = 0;
            ShowSequence();

            if (step >= sequence.Length && measuring)
                CompleteTrial();
        }
    }

    //과제 완료
    void CompleteTrial()
    {
        measuring = false;
        float taskTime = Time.realtimeSinceStartup - startTime;
        panelText.text = "완료";

        WriteLog(taskTime);
        StartCoroutine(RestThenNext());
    }

    //과제 사이 쉬는 시간
    IEnumerator RestThenNext()
    {
        yield return new WaitForSeconds(restTime);
        NextTrial();
    }

    //블록 종료
    void EndBlock()
    {
        blockRunning = false;
        PlacePanel();

        if (block == order.Length - 1) panelText.text = "종료";
        else panelText.text = "잠시 쉬어요";
    }

    //CSV에 한 줄 저장
    void WriteLog(float taskTime)
    {
        string seq = labels[sequence[0]] + "-" + labels[sequence[1]] + "-" + labels[sequence[2]];
        string line = $"{participantNumber},{block + 1},{condition},{trial},{seq},{taskTime:F3},{headTurn:F1}";
        File.AppendAllText(logPath, line + "\n");
    }
}
