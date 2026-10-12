using UnityEngine;

public class Proxemics : MonoBehaviour
{
    //프록시믹스 단계 설정
    [SerializeField] private Camera viewerCamera;
    private float[] stageDistances = { 0.45f, 1.2f, 3.6f, 7.6f }; //프록시믹스 거리
    [SerializeField] private GameObject[] stageLayouts = new GameObject[4]; //스테이지 오브젝트
    private int currentStageIndex = 0; //프로시믹스 단계

    //UI 이동 관련
    private Vector3 moveVelocity; //이동 속도 계산 정보
    [SerializeField] private float smoothTime = 0.5f; //이동 속도
    private Vector3 targetPosition;

    //카메라 고정 y축
    private float fixedY;

    void Start()
    {
        fixedY = transform.position.y;
        float targetDistance = stageDistances[currentStageIndex];
        targetPosition = viewerCamera.transform.position
            + viewerCamera.transform.forward * targetDistance;
        targetPosition.y = fixedY;
        ShowCurrentLayout();
    }



    void Update()
    {
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref moveVelocity,
            smoothTime
            );
    }

    //레이아웃 활성화
    void ShowCurrentLayout()
    {
        for (int i = 0; i < stageLayouts.Length; i++)
        {
            if (stageLayouts[i] == null)
                continue;

            stageLayouts[i].SetActive(i == currentStageIndex);
        }
    }

    public void NextStage()
    {
        if (currentStageIndex >= stageDistances.Length - 1)
            return;

        currentStageIndex++;

        float targetDistance = stageDistances[currentStageIndex];

        targetPosition = viewerCamera.transform.position
            + viewerCamera.transform.forward * targetDistance;

        targetPosition.y = fixedY;

        ShowCurrentLayout();
    }
}
