using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Input;

public class ReadData : MonoBehaviour
{
    // 햅틱 발생시킬 컨트롤러 지정
    OVRInput.Controller leftController = OVRInput.Controller.LTouch;
    OVRInput.Controller rightController = OVRInput.Controller.RTouch;

    //진동 실행
    private void StartHaptics(Handedness handedness) //왼손, 오른족 정보 받는 매개변수
    {
        if (handedness == Handedness.Left && OVRInput.IsControllerConnected(leftController)) //왼손이고 연결되어 있다면
        {
            OVRInput.SetControllerVibration(0.9f, 0.5f, leftController); //(주파수, 세기, 컨트롤러 지정)진동 실행
        }
        if (handedness == Handedness.Right && OVRInput.IsControllerConnected(rightController))
        {
            OVRInput.SetControllerVibration(0.9f, 0.5f, rightController);
        }
    }

    public void HandlePointerEvent(PointerEvent pointerEvent) //Hand Event 객체 전달 받음
    {
        HandRef handData = (HandRef)pointerEvent.Data; //Hand Event 객체의 데이터를 HandRef로 변환 및 변수 담기
        Handedness handedness = handData.Handedness; //Hand Event 객체의 왼손, 오른손 데이터 읽기
        StartHaptics(handedness); //StartHaptics에 전달
    }
}
