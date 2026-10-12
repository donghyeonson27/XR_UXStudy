using Meta.XR.BuildingBlocks;
using Oculus.Interaction.Samples;
using UnityEngine;

public class AngularSizeScalerMake : MonoBehaviour
{
    [SerializeField] private Camera viewerCamera;

    private float referenceDistence = 1f; //기준 거리
    private float targetAngle = 3f; //기준 시야각
    private float ButtonWidth = 60f; //버튼 크기

    private float baseScale; //목표 크기

    //부드러운 변환 애니메이션
    private float scaleVelocity; //부드러운 속도 정보
    [SerializeField] private float smoothTime = 0.15f; //접근 속도

    void Start()
    {
        //목표 크기 계산
        float halfAngled = targetAngle * 0.5f * Mathf.Deg2Rad; //각도를 Radian으로 변환 Mathf.Tan때문
        float worldWidth = 2f * referenceDistence * Mathf.Tan(halfAngled); //계산 크기
        //= 1m거리에서 3도 시야각을 채우려면 계산 크기가  s == 5.236cm(0.05236m)여야 한다.

        //버튼 크기를 기준으로 Scale 계산 및 저장
        baseScale = worldWidth / ButtonWidth;

    }

    void Update()
    {
        //카메라 자동 지정
        if (viewerCamera == null)
        {
            viewerCamera = Camera.main;
            if (viewerCamera == null) return;
        }

        //UI 거리 계산
        float currentDistance = Vector3.Distance(
          viewerCamera.transform.position,
          transform.position
          );

        //거리에 비례해 현재 UI크기 계산
        float currentScale = baseScale * (currentDistance / referenceDistence); //비례 계산

        //UI크기 비례 적용
        float smoothScale = Mathf.SmoothDamp(
            transform.localScale.x,//현재 크기
            currentScale, //목표 크기
            ref scaleVelocity,
            smoothTime
            );

        transform.localScale = Vector3.one * smoothScale;
    }

}
