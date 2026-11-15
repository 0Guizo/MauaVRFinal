using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Placa com o nome do bloco. Quando o usuario chega perto, a placa acende e
/// abre um painel de informacoes. Perto da placa, o gatilho (ou tecla F) fixa o
/// painel aberto/fechado.
/// Edite "descricao" no Inspector para colocar o que funciona em cada bloco.
/// </summary>
public class PlacaBloco : MonoBehaviour
{
    [Header("Conteudo")]
    public string nomeExibicao = "Bloco";
    [TextArea(2, 5)] public string descricao = "";
    public float areaM2;
    public float alturaM;

    [Header("Interacao")]
    public float raioAtivacao = 5f;
    public Color corNormal = new Color(0.05f, 0.22f, 0.45f);
    public Color corDestaque = new Color(0.10f, 0.45f, 0.85f);

    [Header("Referencias (preenchidas pelo MauaVRSetup)")]
    public Renderer painel;
    public Transform painelInfo;
    public TextMeshPro textoInfo;
    public TextMeshPro[] textosNome;

    static InputAction acaoFixar;
    Transform cabeca;
    MaterialPropertyBlock mpb;
    float abertura, destaque;
    bool fixado;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (acaoFixar == null)
        {
            acaoFixar = new InputAction("FixarPlaca", InputActionType.Button);
            acaoFixar.AddBinding("<XRController>{RightHand}/{TriggerButton}");
            acaoFixar.AddBinding("<XRController>{LeftHand}/{TriggerButton}");
            acaoFixar.AddBinding("<Keyboard>/f");
            acaoFixar.AddBinding("<Gamepad>/buttonSouth");
            acaoFixar.Enable();
        }
        AtualizarTextos();
        if (painelInfo) painelInfo.localScale = new Vector3(1f, 0f, 1f);
    }

    void OnValidate() => AtualizarTextos();

    public void AtualizarTextos()
    {
        if (textosNome != null)
            foreach (var t in textosNome) if (t) t.text = nomeExibicao;
        if (textoInfo)
        {
            string dados = $"Área: {areaM2:0} m²\nAltura: {alturaM:0.0} m";
            textoInfo.text = string.IsNullOrWhiteSpace(descricao)
                ? dados
                : $"{descricao}\n<size=70%>Área: {areaM2:0} m²  |  Altura: {alturaM:0.0} m</size>";
        }
    }

    void Update()
    {
        if (cabeca == null) { if (Camera.main) cabeca = Camera.main.transform; else return; }

        Vector3 d = cabeca.position - transform.position; d.y = 0f;
        bool perto = d.magnitude < raioAtivacao;
        if (perto && acaoFixar.WasPressedThisFrame()) fixado = !fixado;
        if (!perto) fixado = false;
        bool aberto = perto || fixado;

        float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
        abertura = Mathf.Lerp(abertura, aberto ? 1f : 0f, k);
        destaque = Mathf.Lerp(destaque, perto ? 1f : 0f, k);

        if (painelInfo) painelInfo.localScale = new Vector3(1f, Mathf.Max(0.0001f, abertura), 1f);
        if (painel)
        {
            var c = Color.Lerp(corNormal, corDestaque, destaque);
            painel.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            painel.SetPropertyBlock(mpb);
        }
    }
}
