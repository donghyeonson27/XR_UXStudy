using Oculus.Interaction.Editor.QuickActions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

//4_Scroll 씬을 한 번에 만들어주는 에디터 도구
//메뉴 : Tools > XR UX Study > Scroll 씬 만들기 (한 번만 실행)
public static class ScrollSceneBuilder
{
    const string SourceScenePath = "Assets/Scenes/1_Targeting.unity";
    const string ScenePath = "Assets/Scenes/4_Scroll.unity";
    const float SectionHeight = 150f; //문단 1개 높이 (15cm)

    //작품 설명 문단 (제목 = 찾을 단어)
    static readonly string[] Titles =
    {
        "작가", "제작 연도", "재료", "크기", "주제", "기법",
        "소리", "빛", "관람 방법", "전시 기간", "소장처", "참고 작품"
    };

    static readonly string[] Bodies =
    {
        "여러 도시를 오가며 빛과 소리를 다루는 미디어 아티스트의 작품입니다.",
        "처음 구상한 뒤 세 번의 전시를 거치며 지금의 모습으로 완성되었습니다.",
        "얇은 천과 금속 프레임, 프로젝터와 여러 개의 스피커로 이루어져 있습니다.",
        "가로 여섯 걸음, 세로 네 걸음 정도의 공간을 작품 하나가 채웁니다.",
        "사라지는 기억과 남아 있는 감각 사이의 거리를 이야기합니다.",
        "실시간으로 바뀌는 영상 위에 손으로 그린 선을 겹쳐서 보여줍니다.",
        "관람객의 움직임에 따라 공간 곳곳에서 낮은 소리가 들려옵니다.",
        "천 사이로 지나가는 빛이 시간에 따라 색과 밝기를 바꿉니다.",
        "작품 안쪽까지 걸어 들어가 천천히 둘러보는 것을 권합니다.",
        "이번 전시는 계절이 바뀌는 동안 약 두 달간 이어집니다.",
        "전시가 끝난 뒤에는 지역 미술관의 소장품으로 보관됩니다.",
        "작가가 영향을 받은 이전 작업들은 전시장 입구에서 볼 수 있습니다."
    };

    [MenuItem("Tools/XR UX Study/Scroll 씬 만들기")]
    static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            EditorUtility.DisplayDialog("Scroll 씬 만들기", "4_Scroll 씬이 이미 있어요.", "확인");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        //1. Targeting 씬을 복사해서 열기 (카메라 리그, 손 추적 세팅 재사용)
        AssetDatabase.CopyAsset(SourceScenePath, ScenePath);
        var scene = EditorSceneManager.OpenScene(ScenePath);

        //2. Select 씬과 같은 기본 정리
        SelectSceneBuilder.RemoveTargetingObjects(scene);
        SelectSceneBuilder.SetupCamera();
        TMP_FontAsset font = SelectSceneBuilder.GetKoreanFont();
        SelectSceneBuilder.CreateExhibitScreen(); //뒤쪽 전시 화면 (분위기용)

        //3. 작품 설명 패널
        GameObject panel = new GameObject("InfoPanel");
        panel.transform.position = new Vector3(0f, 1.3f, 0.45f); //실행하면 머리 기준으로 다시 배치됨

        GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(panel.transform, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(400, 600); //40 × 60cm
        canvasRect.localScale = Vector3.one * 0.001f; //1px = 1mm

        Image background = CreateImage("Background", canvasObject.transform, new Color(0.12f, 0.12f, 0.15f, 0.95f));
        Stretch(background.rectTransform, 0f);

        //스크롤 영역
        GameObject scrollObject = new GameObject("ScrollView", typeof(RectTransform), typeof(ScrollRect));
        scrollObject.transform.SetParent(canvasObject.transform, false);
        Stretch(scrollObject.GetComponent<RectTransform>(), 20f);

        Image viewport = CreateImage("Viewport", scrollObject.transform, new Color(1f, 1f, 1f, 0.02f));
        viewport.raycastTarget = true; //손가락으로 밀 수 있게
        viewport.gameObject.AddComponent<RectMask2D>(); //영역 밖 글 가리기
        Stretch(viewport.rectTransform, 0f);

        GameObject contentObject = new GameObject("Content", typeof(RectTransform));
        contentObject.transform.SetParent(viewport.transform, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, Titles.Length * SectionHeight);
        content.anchoredPosition = Vector2.zero;

        //문단 12개
        RectTransform[] sections = new RectTransform[Titles.Length];
        for (int i = 0; i < Titles.Length; i++)
        {
            string text = $"<b><size=32>{Titles[i]}</size></b>\n{Bodies[i]}";
            TMP_Text section = SelectSceneBuilder.CreateText(contentObject.transform, text, 24, font, Color.white);
            section.name = "Section_" + (i + 1);
            section.alignment = TextAlignmentOptions.Left;

            RectTransform rect = section.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(-30f, SectionHeight);
            rect.anchoredPosition = new Vector2(0f, -SectionHeight * 0.5f - i * SectionHeight);
            sections[i] = rect;
        }

        ScrollRect scrollRect = scrollObject.GetComponent<ScrollRect>();
        scrollRect.content = content;
        scrollRect.viewport = viewport.rectTransform;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped; //끝에서 튕기지 않게

        //가운데 표시 영역 (목표 문단을 맞출 칸)
        Image zone = CreateImage("TargetZone", scrollObject.transform, new Color(1f, 0.85f, 0.2f, 0.2f));
        zone.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        zone.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        zone.rectTransform.sizeDelta = new Vector2(0f, 120f);
        zone.rectTransform.anchoredPosition = Vector2.zero;

        //지시문 (패널 위쪽)
        GameObject promptObject = new GameObject("PromptCanvas", typeof(RectTransform), typeof(Canvas));
        promptObject.transform.SetParent(panel.transform, false);
        promptObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform promptRect = promptObject.GetComponent<RectTransform>();
        promptRect.sizeDelta = new Vector2(700, 80);
        promptRect.localPosition = new Vector3(0f, 0.36f, 0f); //패널 위 6cm
        promptRect.localScale = Vector3.one * 0.001f;
        TMP_Text prompt = SelectSceneBuilder.CreateText(promptObject.transform, "준비", 36, font, Color.white);

        //4. 스크롤 방식 2가지
        QuickActionsAPI.AddPokeCanvasInteraction(canvasObject); //Poke Swipe
        Transform poke = FindChild(canvasObject.transform, "ISDK_PokeCanvasInteraction");

        MicrogestureScroll microgesture = panel.AddComponent<MicrogestureScroll>(); //마이크로 제스처
        SelectSceneBuilder.SetField(microgesture, "hand", FindRightHand());
        SelectSceneBuilder.SetField(microgesture, "scrollRect", scrollRect);

        //5. 실험 진행 오브젝트 연결
        ScrollTrialManager manager = new GameObject("ScrollTrialManager").AddComponent<ScrollTrialManager>();
        SelectSceneBuilder.SetField(manager, "panel", panel.transform);
        SelectSceneBuilder.SetField(manager, "content", content);
        SetArray(manager, "sections", sections);
        SetStringArray(manager, "sectionTitles", Titles);
        SelectSceneBuilder.SetField(manager, "zone", zone.rectTransform);
        SelectSceneBuilder.SetField(manager, "zoneImage", zone);
        SelectSceneBuilder.SetField(manager, "prompt", prompt);
        SelectSceneBuilder.SetField(manager, "head", SelectSceneBuilder.FindInScene("CenterEyeAnchor"));
        SelectSceneBuilder.SetField(manager, "pokeInteraction", poke != null ? poke.gameObject : null);
        SelectSceneBuilder.SetField(manager, "microgestureScroll", microgesture);

        //6. 오른손 Poke만 남기기
        SelectSceneBuilder.KeepOnlyRightHandPoke();

        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Scroll 씬 만들기", "완료했어요. 4_Scroll 씬을 확인해보세요.", "확인");
    }

    static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    //부모 크기에 맞게 늘리기 (padding만큼 안쪽으로)
    static void Stretch(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }
        return null;
    }

    static OVRHand FindRightHand()
    {
        foreach (OVRHand hand in Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Transform t = hand.transform;
            while (t != null)
            {
                if (t.name.ToLower().Contains("right"))
                    return hand;
                t = t.parent;
            }
        }
        return null;
    }

    static void SetArray(Object target, string fieldName, RectTransform[] values)
    {
        var so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(fieldName);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetStringArray(Object target, string fieldName, string[] values)
    {
        var so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(fieldName);
        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).stringValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
