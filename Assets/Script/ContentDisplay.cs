using UnityEngine;

public class ContentDisplay : MonoBehaviour
{
    //전시 화면 설정
    [SerializeField] private Renderer screen; //작품이 보이는 Quad
    [SerializeField] private Color[] contents = { Color.red, Color.green, Color.blue }; //작품 1~3 (색으로 대체)

    public int CurrentIndex { get; private set; } //현재 보이는 작품 번호

    void Start()
    {
        Show(0); //시작은 작품 1
    }

    //작품 전환
    public void Show(int index)
    {
        if (index < 0 || index >= contents.Length)
            return;

        screen.material.color = contents[index];
        CurrentIndex = index;
    }
}
