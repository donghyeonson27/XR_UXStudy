using Oculus.Interaction.Editor.QuickActions;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

//3_Select 씬을 한 번에 구성해주는 에디터 도구
//메뉴 : Tools > XR UX Study > Select 씬 구성하기 (한 번만 실행)
public static class SelectSceneBuilder
{
    const string FontPath = "Assets/Fonts/Pretendard-Medium.ttf";
    const string FontAssetPath = "Assets/Fonts/Pretendard-Medium SDF.asset";
    const string MaterialPath = "Assets/Material/M_Exhibit.mat";

    [MenuItem("Tools/XR UX Study/Select 씬 구성하기")]
    static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "3_Select")
        {
            EditorUtility.DisplayDialog("Select 씬 구성", "3_Select 씬을 먼저 열어주세요.", "확인");
            return;
        }
        if (FindInScene("TrialManager") != null)
        {
            EditorUtility.DisplayDialog("Select 씬 구성", "이미 구성된 씬이에요.", "확인");
            return;
        }

        //1. Targeting 실험용 오브젝트 지우기
        RemoveTargetingObjects(scene);

        //2. 패스스루 끄고 어두운 배경, 바닥 기준 높이
        SetupCamera();

        //3. 전시 화면과 지시문
        TMP_FontAsset font = GetKoreanFont();
        ContentDisplay display = CreateExhibitScreen();
        TMP_Text prompt = CreatePrompt(font);

        //4. 실험 진행 오브젝트
        TrialManager trialManager = new GameObject("TrialManager").AddComponent<TrialManager>();
        Transform head = FindInScene("CenterEyeAnchor");
        Transform leftHand = FindInScene("LeftHandAnchor");

        //5. 같은 메뉴 2개 (플로팅 / 손)
        GameObject floatingMenu = CreateMenu("Menu_Floating", font, trialManager);
        floatingMenu.transform.position = new Vector3(0f, 1.2f, 0.4f); //실행하면 머리 기준으로 다시 배치됨

        GameObject handMenu = CreateMenu("Menu_Hand", font, trialManager);
        HandMenuFollow follow = handMenu.AddComponent<HandMenuFollow>();
        SetField(follow, "hand", leftHand);
        SetField(follow, "head", head);

        //6. TrialManager 연결
        SetField(trialManager, "display", display);
        SetField(trialManager, "prompt", prompt);
        SetField(trialManager, "head", head);
        SetField(trialManager, "floatingMenu", floatingMenu.transform);
        SetField(trialManager, "handMenu", handMenu);

        //7. 오른손 Poke만 남기기 (메뉴를 만들 때 인터랙터가 추가될 수 있어서 마지막에)
        KeepOnlyRightHandPoke();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Select 씬 구성", "완료했어요. Hierarchy를 확인해보세요.", "확인");
    }

    public static void RemoveTargetingObjects(UnityEngine.SceneManagement.Scene scene)
    {
        string[] names = { "EmptyUIBackplateWithCanvas", "Poke Interaction", "HandRayInteractor" };
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (string name in names)
            {
                if (root.name == name)
                {
                    Object.DestroyImmediate(root);
                    break; //지운 오브젝트는 더 확인하지 않음
                }
            }
        }
    }

    public static void SetupCamera()
    {
        Transform passthrough = FindInScene("[BuildingBlock] Passthrough");
        if (passthrough != null)
            passthrough.gameObject.SetActive(false);

        Camera cam = FindInScene("CenterEyeAnchor").GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 1f); //Alpha 1 (= 255)
        EditorUtility.SetDirty(cam);

        //OVRManager의 Tracking Origin Type = Floor Level
        Transform rig = FindInScene("[BuildingBlock] Camera Rig");
        if (rig != null)
        {
            Component manager = rig.GetComponent("OVRManager");
            if (manager != null)
            {
                var so = new SerializedObject(manager);
                SerializedProperty origin = so.FindProperty("_trackingOriginType");
                if (origin != null)
                {
                    origin.enumValueIndex = 1; //0 : EyeLevel, 1 : FloorLevel
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }

    public static TMP_FontAsset GetKoreanFont()
    {
        TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (fontAsset != null)
            return fontAsset;

        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null)
            return null;

        //Dynamic 폰트 에셋 만들기 (필요한 글자를 그때그때 추가)
        fontAsset = TMP_FontAsset.CreateFontAsset(font);
        fontAsset.name = "Pretendard-Medium SDF";
        AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
        fontAsset.material.name = fontAsset.name + " Material";
        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        fontAsset.atlasTexture.name = fontAsset.name + " Atlas";
        AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
        AssetDatabase.SaveAssets();
        return fontAsset;
    }

    public static ContentDisplay CreateExhibitScreen()
    {
        GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "ExhibitScreen";
        screen.transform.position = new Vector3(0f, 1.6f, 2.5f);
        screen.transform.localScale = new Vector3(3f, 1.7f, 1f);

        //Unlit 머티리얼 (조명 영향 없이 색이 그대로 보임)
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        Renderer screenRenderer = screen.GetComponent<Renderer>();
        screenRenderer.sharedMaterial = material;

        ContentDisplay display = screen.AddComponent<ContentDisplay>();
        SetField(display, "screen", screenRenderer);
        return display;
    }

    static TMP_Text CreatePrompt(TMP_FontAsset font)
    {
        GameObject canvasObject = new GameObject("PromptCanvas", typeof(RectTransform), typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        RectTransform rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1600, 250);
        rect.position = new Vector3(0f, 2.65f, 2.45f); //스크린 위쪽
        rect.localScale = Vector3.one * 0.001f; //1px = 1mm

        return CreateText(canvasObject.transform, "준비", 120, font, Color.white);
    }

    static GameObject CreateMenu(string name, TMP_FontAsset font, TrialManager trialManager)
    {
        GameObject root = new GameObject(name);

        //World Space Canvas
        GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(root.transform, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(170, 60);
        canvasRect.localScale = Vector3.one * 0.001f; //1px = 1mm → 패널 17 × 6cm

        //배경 판
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.15f, 0.15f, 0.18f, 0.9f);
        panelImage.raycastTarget = false;

        //버튼 3개 (작품 1, 2, 3)
        for (int i = 0; i < 3; i++)
            CreateButton(canvasObject.transform, i, font, trialManager);

        //Poke로 누를 수 있게 (Add Poke Interaction to Canvas와 같은 기능)
        QuickActionsAPI.AddPokeCanvasInteraction(canvasObject);

        return root;
    }

    static void CreateButton(Transform parent, int index, TMP_FontAsset font, TrialManager trialManager)
    {
        GameObject buttonObject = new GameObject("Button_" + (index + 1), typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(40, 40); //4 × 4cm
        rect.anchoredPosition = new Vector2((index - 1) * 50f, 0f); //간격 1cm

        //버튼 상태별 색 (Default → Hover → Pressed)
        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.85f, 0.85f, 0.9f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.4f, 0.7f, 1f);
        button.colors = colors;

        CreateText(buttonObject.transform, (index + 1).ToString(), 24, font, new Color(0.1f, 0.1f, 0.12f));

        //OnClick → TrialManager.OnMenuButton(index)
        UnityEventTools.AddIntPersistentListener(button.onClick, trialManager.OnMenuButton, index);
    }

    public static TMP_Text CreateText(Transform parent, string content, float size, TMP_FontAsset font, Color color)
    {
        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.text = content;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    public static void KeepOnlyRightHandPoke()
    {
        foreach (MonoBehaviour component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (component == null)
                continue;

            string type = component.GetType().Name;
            bool isRay = type.Contains("RayInteractor");
            bool isGrab = type.Contains("GrabInteractor");
            bool isLeftPoke = type == "PokeInteractor" && IsLeft(component.transform);

            if (isRay || isGrab || isLeftPoke)
                component.gameObject.SetActive(false);
        }
    }

    static bool IsLeft(Transform t)
    {
        while (t != null)
        {
            if (t.name.Contains("Left"))
                return true;
            t = t.parent;
        }
        return false;
    }

    public static Transform FindInScene(string name)
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name == name)
                return t;
        }
        return null;
    }

    public static void SetField(Object target, string fieldName, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(fieldName).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
