using UnityEngine;
using UnityEngine.UI;

public class MicrogestureScroll : MonoBehaviour
{
    //연결할 오브젝트
    [SerializeField] private OVRHand hand; //오른손
    [SerializeField] private ScrollRect scrollRect; //스크롤할 패널

    //움직임 설정
    [SerializeField] private float stepPixels = 100f; //엄지를 한 번 밀 때 움직이는 거리 (1px = 1mm)
    [SerializeField] private float moveSpeed = 1500f; //움직이는 속도 (px/초)

    private float targetY; //가야 할 위치

    void OnEnable()
    {
        //켜질 때 지금 위치에서 시작
        targetY = scrollRect.content.anchoredPosition.y;
    }

    void Update()
    {
        if (hand == null)
            return;

        //엄지 마이크로 제스처 읽기
        OVRHand.MicrogestureType gesture = hand.GetMicrogestureType();

        if (gesture == OVRHand.MicrogestureType.SwipeForward)
            targetY += stepPixels; //앞으로 밀기 → 아래 내용 보기
        else if (gesture == OVRHand.MicrogestureType.SwipeBackward)
            targetY -= stepPixels; //뒤로 당기기 → 위 내용 보기

        //글 범위 밖으로 나가지 않게
        float maxY = scrollRect.content.rect.height - scrollRect.viewport.rect.height;
        targetY = Mathf.Clamp(targetY, 0f, maxY);

        //목표 위치까지 부드럽게 이동
        Vector2 position = scrollRect.content.anchoredPosition;
        position.y = Mathf.MoveTowards(position.y, targetY, moveSpeed * Time.deltaTime);
        scrollRect.content.anchoredPosition = position;
    }
}
