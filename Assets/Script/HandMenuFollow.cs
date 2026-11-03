using UnityEngine;

public class HandMenuFollow : MonoBehaviour
{
    //따라갈 대상
    [SerializeField] private Transform hand; //LeftHandAnchor
    [SerializeField] private Transform head; //CenterEyeAnchor

    //손에서 띄우는 거리
    [SerializeField] private float sideOffset = 0.07f; //손 오른쪽으로
    [SerializeField] private float upOffset = 0.05f; //손 위로

    void LateUpdate()
    {
        if (hand == null || head == null)
            return;

        //머리 기준 오른쪽 방향 (수평)
        Vector3 right = head.right;
        right.y = 0;
        right.Normalize();

        //왼손 오른쪽 위에 메뉴를 두고, 머리를 바라보게
        transform.position = hand.position + right * sideOffset + Vector3.up * upOffset;
        transform.rotation = Quaternion.LookRotation(transform.position - head.position);
    }
}
