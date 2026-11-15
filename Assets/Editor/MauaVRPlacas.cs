using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Cria uma placa interativa na frente de cada bloco do campus.
/// Posicoes dos blocos: Assets/Mapa/blocos.json (gerado a partir do mapa oficial + malha).
public static class MauaVRPlacas
{
    const string JsonPath = "Assets/Mapa/blocos.json";
    const string MatDir = "Assets/Mapa/Materiais";
    const string TmpPackage = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

    [System.Serializable] class Bloco { public string nome; public float xmin, xmax, zmin, zmax, chao, topo, area, letraX, letraZ; public float[] pegada; }
    [System.Serializable] class Lista { public Bloco[] blocos; }

    static readonly Dictionary<string, string> NomesBonitos = new Dictionary<string, string>
    {
        { "Ginasio", "Ginásio" }, { "Centro de Vivencia", "Centro de Vivência" }, { "Portaria 1", "Portaria 1" },
    };

    static readonly Dictionary<string, float> BonusChao = new Dictionary<string, float>
    {
        { "calcada", 3f }, { "pavimento_concreto", 3f }, { "pavimento_asfalto", 1f }, { "terreno", 1f },
        { "acostamento", 0f }, { "lombada", -3f }, { "meio_fio", -2f }, { "sarjeta", -2f }, { "pista", -3f },
    };

    [MenuItem("MauaVR/4. Criar Placas dos Blocos")]
    public static void CriarPlacas()
    {
        if (CarregarFonte() == null)
        {
            Debug.Log("[MauaVR] Importando TMP Essential Resources...");
            AssetDatabase.importPackageCompleted += AoImportar;
            AssetDatabase.ImportPackage(TmpPackage, false);
            return;
        }
        Gerar();
    }

    static void AoImportar(string _)
    {
        AssetDatabase.importPackageCompleted -= AoImportar;
        AssetDatabase.Refresh();
        Gerar();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static TMP_FontAsset CarregarFonte()
    {
        var guid = AssetDatabase.FindAssets("LiberationSans SDF t:TMP_FontAsset").FirstOrDefault();
        return guid == null ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
    }

    static void Gerar()
    {
        var fonte = CarregarFonte();
        if (fonte == null) { Debug.LogError("[MauaVR] Fonte TMP nao encontrada."); return; }

        var cena = SceneManager_Abrir();
        var lista = JsonUtility.FromJson<Lista>(File.ReadAllText(JsonPath));
        foreach (var b in lista.blocos)   // o importador OBJ da Unity espelha o eixo X
        {
            (b.xmin, b.xmax) = (-b.xmax, -b.xmin);
            b.letraX = -b.letraX;
            for (int i = 0; i < b.pegada.Length; i += 2) b.pegada[i] = -b.pegada[i];
        }

        // guarda descricoes ja escritas pelo usuario antes de recriar
        var descricoes = new Dictionary<string, string>();
        var antigo = GameObject.Find("Placas dos Blocos");
        if (antigo)
        {
            foreach (var p in antigo.GetComponentsInChildren<PlacaBloco>())
                if (!string.IsNullOrWhiteSpace(p.descricao)) descricoes[p.nomeExibicao] = p.descricao;
            Object.DestroyImmediate(antigo);
        }

        var raiz = new GameObject("Placas dos Blocos").transform;
        var matPoste = Mat("_placa_poste", new Color(0.35f, 0.37f, 0.40f));
        var matPainel = Mat("_placa_painel", new Color(0.05f, 0.22f, 0.45f));
        var matInfo = Mat("_placa_info", new Color(0.93f, 0.94f, 0.96f));

        Physics.SyncTransforms();
        int ok = 0;
        foreach (var b in lista.blocos)
        {
            string nome = NomesBonitos.TryGetValue(b.nome, out var n) ? n : $"Bloco {b.nome}";
            if (!AcharFrente(b, out Vector3 pos, out Vector3 normal, out string chao))
            {
                Debug.LogWarning($"[MauaVR] {nome}: sem posicao boa, usando a borda do predio.");
                pos = new Vector3((b.xmin + b.xmax) / 2f, b.chao, b.zmin - 2f);
                normal = Vector3.back; chao = "?";
            }
            var placa = CriarPlaca(nome, pos, normal, fonte, matPoste, matPainel, matInfo);
            placa.transform.SetParent(raiz, true);
            var comp = placa.GetComponent<PlacaBloco>();
            comp.areaM2 = b.area;
            comp.alturaM = b.topo - b.chao;
            if (descricoes.TryGetValue(nome, out var desc)) comp.descricao = desc;
            comp.AtualizarTextos();
            ok++;
            Debug.Log($"[MauaVR] Placa '{nome}' em {pos} (chao: {chao})");
        }

        EditorSceneManager.MarkSceneDirty(cena);
        EditorSceneManager.SaveScene(cena);
        Debug.Log($"[MauaVR] {ok} placas criadas.");
    }

    static UnityEngine.SceneManagement.Scene SceneManager_Abrir()
    {
        const string path = "Assets/Scenes/CampusMaua.unity";
        var ativa = EditorSceneManager.GetActiveScene();
        if (ativa.path == path) return ativa;
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        return EditorSceneManager.OpenScene(path);
    }

    /// Raios horizontais de fora para dentro ate a parede do predio; a placa vai 2,2 m
    /// para fora da parede, de preferencia sobre calcada/concreto e fora da pista.
    static bool AcharFrente(Bloco b, out Vector3 melhorPos, out Vector3 melhorNormal, out string melhorChao)
    {
        var c = new Vector3((b.xmin + b.xmax) / 2f, 0, (b.zmin + b.zmax) / 2f);
        float raio = new Vector2(b.xmax - b.xmin, b.zmax - b.zmin).magnitude / 2f + 8f;
        float y = b.chao + 1.5f;
        float melhor = float.MinValue;
        melhorPos = default; melhorNormal = default; melhorChao = null;

        for (int ang = 0; ang < 360; ang += 5)
        {
            var dir = Quaternion.Euler(0, ang, 0) * Vector3.forward;
            var origem = c + dir * raio; origem.y = y;
            if (!Physics.Raycast(origem, -dir, out var parede, raio * 2f)) continue;
            if (parede.collider.gameObject.name != "edificacoes") continue;
            var pw = parede.point;
            if (DistanciaPegada(b.pegada, new Vector2(pw.x, pw.z)) > 0.5f) continue;   // parede de OUTRO predio

            var nrm = Vector3.ProjectOnPlane(parede.normal, Vector3.up).normalized;
            if (nrm.sqrMagnitude < 0.5f) continue;
            var p = pw + nrm * 2.2f;
            if (!Physics.Raycast(new Vector3(p.x, b.topo + 5f, p.z), Vector3.down, out var solo, b.topo + 50f)) continue;
            string tipo = solo.collider.gameObject.name;
            if (!BonusChao.TryGetValue(tipo, out float bonus)) continue;           // telhado, abrigo...
            if (solo.point.y < b.chao - 1.5f || solo.point.y > b.chao + 2.0f) continue;
            p = solo.point;
            // espaco livre para poste e painel
            if (Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 2.4f, 0.45f)) continue;

            float score = bonus - 0.03f * Vector3.Distance(new Vector3(pw.x, 0, pw.z), c);
            if (score > melhor) { melhor = score; melhorPos = p; melhorNormal = nrm; melhorChao = tipo; }
        }
        return melhorChao != null;
    }

    /// Distancia 2D do ponto ate a pegada (triangulos da laje); 0 se estiver dentro.
    static float DistanciaPegada(float[] t, Vector2 p)
    {
        float melhor = float.MaxValue;
        for (int i = 0; i + 5 < t.Length; i += 6)
        {
            var a = new Vector2(t[i], t[i + 1]); var b = new Vector2(t[i + 2], t[i + 3]); var c = new Vector2(t[i + 4], t[i + 5]);
            float d1 = Cruz(a, b, p), d2 = Cruz(b, c, p), d3 = Cruz(c, a, p);
            if ((d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0)) return 0f;
            melhor = Mathf.Min(melhor, Mathf.Min(DistSeg(p, a, b), Mathf.Min(DistSeg(p, b, c), DistSeg(p, c, a))));
        }
        return melhor;
    }
    static float Cruz(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-9f));
        return Vector2.Distance(p, a + ab * t);
    }

    static GameObject CriarPlaca(string nome, Vector3 pos, Vector3 normal, TMP_FontAsset fonte,
                                 Material matPoste, Material matPainel, Material matInfo)
    {
        var go = new GameObject($"Placa - {nome}");
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-normal, Vector3.up));
        var comp = go.AddComponent<PlacaBloco>();
        comp.nomeExibicao = nome;

        var poste = Cubo("Poste", go.transform, new Vector3(0, 1.2f, 0.06f), new Vector3(0.08f, 2.4f, 0.08f), matPoste);
        var col = go.AddComponent<BoxCollider>();                         // nao deixa atravessar o poste
        col.center = new Vector3(0, 1.2f, 0.06f); col.size = new Vector3(0.15f, 2.4f, 0.15f);

        var painel = Cubo("Painel", go.transform, new Vector3(0, 2.45f, 0), new Vector3(1.7f, 0.6f, 0.06f), matPainel);
        comp.painel = painel.GetComponent<Renderer>();
        comp.textosNome = new[]
        {
            Texto("Nome (frente)", go.transform, new Vector3(0, 2.45f, -0.035f), Quaternion.identity, new Vector2(1.55f, 0.48f), fonte, Color.white, 3.2f),
            Texto("Nome (verso)", go.transform, new Vector3(0, 2.45f, 0.035f), Quaternion.Euler(0, 180, 0), new Vector2(1.55f, 0.48f), fonte, Color.white, 3.2f),
        };
        foreach (var t in comp.textosNome) t.fontStyle = FontStyles.Bold;

        // painel de informacoes: abre de cima para baixo, logo abaixo do nome
        var info = new GameObject("Info").transform;
        info.SetParent(go.transform, false);
        info.localPosition = new Vector3(0, 2.13f, 0);
        Cubo("Fundo", info, new Vector3(0, -0.3f, 0), new Vector3(1.6f, 0.6f, 0.04f), matInfo);
        comp.textoInfo = Texto("Texto", info, new Vector3(0, -0.3f, -0.025f), Quaternion.identity, new Vector2(1.45f, 0.52f), fonte, new Color(0.08f, 0.12f, 0.2f), 1.4f);
        comp.textoInfo.enableAutoSizing = true;
        comp.textoInfo.fontSizeMin = 0.4f; comp.textoInfo.fontSizeMax = 1.4f;
        comp.painelInfo = info;

        GameObjectUtility.SetStaticEditorFlags(poste, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static GameObject Cubo(string nome, Transform pai, Vector3 pos, Vector3 escala, Material mat)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        c.name = nome;
        Object.DestroyImmediate(c.GetComponent<Collider>());
        c.transform.SetParent(pai, false);
        c.transform.localPosition = pos;
        c.transform.localScale = escala;
        c.GetComponent<Renderer>().sharedMaterial = mat;
        return c;
    }

    static TextMeshPro Texto(string nome, Transform pai, Vector3 pos, Quaternion rot, Vector2 tamanho,
                             TMP_FontAsset fonte, Color cor, float fontSize)
    {
        var go = new GameObject(nome);
        go.transform.SetParent(pai, false);
        var t = go.AddComponent<TextMeshPro>();
        t.rectTransform.localPosition = pos;
        t.rectTransform.localRotation = rot;
        t.rectTransform.sizeDelta = tamanho;
        t.font = fonte;
        t.color = cor;
        t.fontSize = fontSize;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.5f; t.fontSizeMax = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    static Material Mat(string nome, Color cor)
    {
        Directory.CreateDirectory(MatDir);
        string path = $"{MatDir}/{nome}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = cor };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", cor);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.3f);
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }
}
