using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Gera imagens de preview da cena (vista aerea e vista do usuario) em Imagens/.
public static class MauaVRCaptura
{
    [MenuItem("MauaVR/Capturar Imagens de Preview")]
    public static void Capturar()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/CampusMaua.unity");
        var rig = GameObject.Find("XR Origin (Rig)").transform;
        Directory.CreateDirectory("Imagens");

        var go = new GameObject("CapturaCam");
        var cam = go.AddComponent<Camera>();
        cam.farClipPlane = 3000f;

        go.transform.position = new Vector3(0, 380, -330);
        go.transform.LookAt(Vector3.zero);
        Salvar(cam, "Imagens/aerea.png");

        go.transform.position = rig.position + Vector3.up * 1.65f;
        for (int i = 0; i < 4; i++)
        {
            go.transform.rotation = Quaternion.Euler(5f, i * 90f, 0f);
            Salvar(cam, $"Imagens/usuario_{i * 90}.png");
        }
        Object.DestroyImmediate(go);
    }

    [MenuItem("MauaVR/Capturar Imagens das Placas")]
    public static void CapturarPlacas()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/CampusMaua.unity");
        Directory.CreateDirectory("Imagens");
        var go = new GameObject("CapturaCam");
        var cam = go.AddComponent<Camera>();
        cam.farClipPlane = 3000f;
        foreach (var p in Object.FindObjectsByType<PlacaBloco>(FindObjectsSortMode.None))
        {
            var t = p.transform;
            go.transform.position = t.position - t.forward * 6f + t.right * 1.5f + Vector3.up * 1.7f;
            go.transform.LookAt(t.position + Vector3.up * 2.1f);
            Salvar(cam, $"Imagens/placa_{p.nomeExibicao.Replace(' ', '_')}.png");
        }
        Object.DestroyImmediate(go);
    }

    static void Salvar(Camera cam, string path)
    {
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("[MauaVR] Captura: " + path);
    }
}
