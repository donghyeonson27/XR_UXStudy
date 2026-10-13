using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic; 

public class DwellSelectButton : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler
{
    [Header("Dwell 설정")]
    [SerializeField] private float dwellDuration = 1.5f;

    [Header("Dwell 색상")]
    [SerializeField] private Color dwellStartColor = Color.cyan;
    [SerializeField] private Color dwellCompleteColor = Color.green;

    // 현재 응시 상태
    private bool isLooking = false;

    // 누적 응시 시간
    private float dwellTimer = 0f;

    // 이미 선택했는지 확인
    private bool hasSelected = false;

    // 현재 버튼 컴포넌트
    private Button button;

    // 버튼의 원래 색상
    private Color originalColor;

    // 버튼 위에 올라와 있는 포인터 id 목록 (양손 시선 포인터 각각)
    private readonly HashSet<int> pointersInside = new HashSet<int>();
    private bool IsLooking => pointersInside.Count > 0;

    private void Awake()
    {
        // 현재 오브젝트에서 Button 컴포넌트 가져오기
        button = GetComponent<Button>();

        // Button이 없으면 오류를 표시하고 스크립트 비활성화
        if (button == null)
        {
            Debug.LogError(
                "Button 컴포넌트를 찾을 수 없습니다: " + gameObject.name
            );

            enabled = false;
            return;
        }

        // 버튼의 원래 색상 저장
        if (button.targetGraphic != null)
        {
            originalColor = button.targetGraphic.color;
        }
        else
        {
            Debug.LogWarning(
                "Button의 Target Graphic이 설정되지 않았습니다: "
                + gameObject.name
            );
        }

        // 0초 이하의 응시 시간 방지
        dwellDuration = Mathf.Max(0.1f, dwellDuration);
    }


    // 포인터가 버튼에 들어왔을 때
    public void OnPointerEnter(PointerEventData eventData)
    {
        bool wasLooking = IsLooking;
        pointersInside.Add(eventData.pointerId);

        if (!wasLooking)              // 첫 포인터일 때만 새 응시 시작
        {
            dwellTimer = 0f;
            hasSelected = false;
            SetTargetColor(dwellStartColor);
            Debug.Log("응시 시작: " + gameObject.name);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointersInside.Remove(eventData.pointerId);

        if (!IsLooking)               // 마지막 포인터가 나갔을 때만 리셋
        {
            dwellTimer = 0f;
            hasSelected = false;
            SetTargetColor(originalColor);
            Debug.Log("응시 종료: " + gameObject.name);
        }
    }

    private void OnDisable()
    {
        pointersInside.Clear();
        dwellTimer = 0f;
        hasSelected = false;
        SetTargetColor(originalColor);
    }

    private void Update()
    {
        // 응시 중이 아니거나 이미 선택했다면 실행 중단
        if (!IsLooking || hasSelected)
        {
            return;
        }

        // 버튼이 없거나 상호작용할 수 없다면 중단
        if (button == null || !button.interactable)
        {
            return;
        }

        // 현재 프레임의 경과 시간을 누적
        dwellTimer += Time.unscaledDeltaTime;

        // 응시 진행률 계산: 0~1 범위
        float progress = Mathf.Clamp01(
            dwellTimer / dwellDuration
        );

        // 진행률에 따라 색상 변경
        Color currentColor = Color.Lerp(
            dwellStartColor,
            dwellCompleteColor,
            progress
        );

        SetTargetColor(currentColor);

        // 기준 응시 시간에 도달했는지 확인
        if (dwellTimer >= dwellDuration)
        {
            // 먼저 선택 완료 상태로 변경해 중복 실행 방지
            hasSelected = true;

            Debug.Log("Dwell 선택 완료: " + gameObject.name);

            // Button의 On Click 이벤트 실행
            button.onClick.Invoke();
        }
    }


    // Target Graphic의 색상 변경
    private void SetTargetColor(Color color)
    {
        if (button != null && button.targetGraphic != null)
        {
            button.targetGraphic.color = color;
        }
    }
}