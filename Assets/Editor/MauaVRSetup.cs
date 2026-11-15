using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

/// Monta o projeto MauaVR automaticamente. Menu: MauaVR > ...
public static class MauaVRSetup
{
    const string ObjPath = "Assets/Mapa/campus_maua.obj";
    const string MtlPath = "Assets/Mapa/campus_maua.mtl";
    const string MatDir = "Assets/Mapa/Materiais";
    const string ScenePath = "Assets/Scenes/CampusMaua.unity";

    // Objetos do mapa que NAO devem ter colisao (pinturas finas e copas das arvores)
    static readonly HashSet<string> SemColisao = new HashSet<string>
        { "props_copa", "pintura_branca", "pintura_amarela", "faixa_pedestre", "trajeto" };

    [MenuItem("MauaVR/Configurar Tudo (XR + Materiais + Cena)")]
    public static void ConfigurarTudo()
    {
        ConfigurarXR();
        CriarMateriais();
        CriarCena();
        Debug.Log("[MauaVR] Configuracao concluida.");
    }

    // ------------------------------------------------------------------ XR
    [MenuItem("MauaVR/1. Configurar OpenXR")]
    public static void ConfigurarXR()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget porPlataforma) || porPlataforma == null)
        {
            Directory.CreateDirectory("Assets/XR");
            porPlataforma = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(porPlataforma, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, porPlataforma, true);
        }

        var grupo = BuildTargetGroup.Standalone;
        if (!porPlataforma.HasSettingsForBuildTarget(grupo))
            porPlataforma.CreateDefaultSettingsForBuildTarget(grupo);
        if (!porPlataforma.HasManagerSettingsForBuildTarget(grupo))
            porPlataforma.CreateDefaultManagerSettingsForBuildTarget(grupo);

        var geral = porPlataforma.SettingsForBuildTarget(grupo);
        geral.InitManagerOnStart = true;
        bool ok = XRPackageMetadataStore.AssignLoader(geral.AssignedSettings,
            "UnityEngine.XR.OpenXR.OpenXRLoader", grupo);
        Debug.Log($"[MauaVR] OpenXR loader (PC) atribuido: {ok}");

        UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(grupo);
        var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(grupo);
        if (oxr != null)
        {
            var perfis = new[] { "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile",
                                 "MetaQuestTouchProControllerProfile", "KHRSimpleControllerProfile" };
            foreach (var f in oxr.GetFeatures<OpenXRInteractionFeature>())
            {
                if (perfis.Contains(f.GetType().Name))
                {
                    f.enabled = true;
                    EditorUtility.SetDirty(f);
                    Debug.Log($"[MauaVR] Perfil de controle habilitado: {f.GetType().Name}");
                }
            }
            EditorUtility.SetDirty(oxr);
        }
        else Debug.LogWarning("[MauaVR] OpenXRSettings nao encontrado (abra Project Settings > XR Plug-in Management).");

        EditorUtility.SetDirty(porPlataforma);
        AssetDatabase.SaveAssets();
    }

    // ------------------------------------------------------------ Materiais
    [MenuItem("MauaVR/2. Criar Materiais do Mapa")]
    public static void CriarMateriais()
    {
        Directory.CreateDirectory(MatDir);
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        string nome = null;
        foreach (var linha in File.ReadAllLines(MtlPath))
        {
            var p = linha.Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            if (p[0] == "newmtl") nome = p[1];
            else if (p[0] == "Kd" && nome != null)
            {
                var c = new Color(F(p[1]), F(p[2]), F(p[3]));
                string path = $"{MatDir}/{nome}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
                mat.shader = shader;
                mat.color = c;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
                EditorUtility.SetDirty(mat);
            }
        }
        AssetDatabase.SaveAssets();
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    // ----------------------------------------------------------------- Cena
    [MenuItem("MauaVR/3. Criar Cena CampusMaua")]
    public static void CriarCena()
    {
        AssetDatabase.ImportAsset(ObjPath, ImportAssetOptions.ForceUpdate);
        var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(ObjPath);
        if (modelo == null) { Debug.LogError("[MauaVR] campus_maua.obj nao encontrado em " + ObjPath); return; }

        var cena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Luz
        var luz = new GameObject("Sol").AddComponent<Light>();
        luz.type = LightType.Directional;
        luz.intensity = 1.3f;
        luz.shadows = LightShadows.Soft;
        luz.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        RenderSettings.sun = luz;

        // Mapa
        var mapa = (GameObject)PrefabUtility.InstantiatePrefab(modelo);
        mapa.name = "CampusMaua";
        foreach (var r in mapa.GetComponentsInChildren<MeshRenderer>())
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{r.gameObject.name}.mat");
            if (mat != null) r.sharedMaterial = mat;
            else Debug.LogWarning($"[MauaVR] Sem material para '{r.gameObject.name}'");
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            if (SemColisao.Contains(r.gameObject.name))
                foreach (var col in r.GetComponents<Collider>()) Object.DestroyImmediate(col);
        }
        var b = new Bounds(mapa.transform.position, Vector3.zero);
        foreach (var r in mapa.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        Debug.Log($"[MauaVR] Bounds do mapa: centro={b.center} tamanho={b.size}");

        // Ponto de inicio: calcada mais proxima do centro do campus
        Physics.SyncTransforms();
        Vector3 inicio = EncontrarInicio(b);
        Debug.Log($"[MauaVR] Ponto inicial: {inicio}");

        // Rig VR
        var rig = new GameObject("XR Origin (Rig)");
        rig.transform.position = inicio;
        var cc = rig.AddComponent<CharacterController>();
        cc.radius = 0.25f; cc.height = 1.75f; cc.center = new Vector3(0, 0.9f, 0);
        cc.stepOffset = 0.35f; cc.slopeLimit = 50f; cc.skinWidth = 0.04f;

        var offset = new GameObject("Camera Offset").transform;
        offset.SetParent(rig.transform, false);

        var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
        camGO.transform.SetParent(offset, false);
        camGO.transform.localPosition = new Vector3(0, 1.65f, 0);
        var cam = camGO.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000f;
        camGO.AddComponent<AudioListener>();
        ConfigurarTPD(camGO.AddComponent<TrackedPoseDriver>(), "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation", "<XRHMD>/trackingState");

        var origin = rig.AddComponent<XROrigin>();
        origin.Origin = rig;
        origin.CameraFloorOffsetObject = offset.gameObject;
        origin.Camera = cam;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 1.65f;

        CriarMao(offset, "Mao Esquerda", "LeftHand", new Color(0.2f, 0.5f, 1f));
        CriarMao(offset, "Mao Direita", "RightHand", new Color(1f, 0.4f, 0.2f));

        var loco = rig.AddComponent<LocomocaoVR>();
        loco.cabeca = camGO.transform;

        EditorSceneManager.SaveScene(cena, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[MauaVR] Cena salva em " + ScenePath);
    }

    static Vector3 EncontrarInicio(Bounds b)
    {
        float topo = b.max.y + 50f;
        Vector3 melhor = Vector3.zero; float melhorDist = float.MaxValue; bool achouCalcada = false;
        Vector3 qualquer = new Vector3(b.center.x, 0, b.center.z); bool achouQualquer = false;
        for (float x = b.min.x; x <= b.max.x; x += 4f)
        for (float z = b.min.z; z <= b.max.z; z += 4f)
        {
            if (!Physics.Raycast(new Vector3(x, topo, z), Vector3.down, out var hit, topo - b.min.y + 10f)) continue;
            float d = new Vector2(x - b.center.x, z - b.center.z).sqrMagnitude;
            string n = hit.collider.gameObject.name;
            if (!achouQualquer || d < (qualquer - b.center).sqrMagnitude) { qualquer = hit.point; achouQualquer = true; }
            if ((n == "calcada" || n == "pavimento_concreto") && d < melhorDist) { melhorDist = d; melhor = hit.point; achouCalcada = true; }
        }
        Vector3 p = achouCalcada ? melhor : qualquer;
        return p + Vector3.up * 0.05f;
    }

    static void CriarMao(Transform pai, string nome, string mao, Color cor)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = nome;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(pai, false);
        go.transform.localScale = Vector3.one * 0.08f;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        string path = $"{MatDir}/_{mao}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader) { color = cor }; AssetDatabase.CreateAsset(mat, path); }
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        ConfigurarTPD(go.AddComponent<TrackedPoseDriver>(),
            $"<XRController>{{{mao}}}/devicePosition",
            $"<XRController>{{{mao}}}/deviceRotation",
            $"<XRController>{{{mao}}}/trackingState");
    }

    static void ConfigurarTPD(TrackedPoseDriver tpd, string pos, string rot, string estado)
    {
        tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        tpd.positionInput = new InputActionProperty(new InputAction("Posicao", InputActionType.Value, pos, expectedControlType: "Vector3"));
        tpd.rotationInput = new InputActionProperty(new InputAction("Rotacao", InputActionType.Value, rot, expectedControlType: "Quaternion"));
        tpd.trackingStateInput = new InputActionProperty(new InputAction("Estado", InputActionType.Value, estado, expectedControlType: "Integer"));
        EditorUtility.SetDirty(tpd);
    }
}
