using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Locomocao para andar pelo campus em VR (e tambem no PC, para testar sem oculos).
///
/// VR (Quest via Link / OpenXR):
///   - Analogico ESQUERDO: anda (na direcao para onde voce esta olhando)
///   - Clicar no analogico ESQUERDO: corre
///   - Analogico DIREITO: gira 45 graus (snap turn)
///   - Botao B: volta para o ponto inicial
///
/// PC (sem oculos):
///   - WASD / setas: anda    - Shift: corre
///   - Botao direito do mouse segurado: olhar em volta
///   - Q / E: gira 45 graus  - R: volta para o ponto inicial
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class LocomocaoVR : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Camera principal (a cabeca do usuario).")]
    public Transform cabeca;

    [Header("Movimento")]
    public float velocidade = 2.0f;
    public float velocidadeCorrida = 5.0f;
    public float gravidade = -9.81f;

    [Header("Rotacao")]
    public bool giroSuave = false;
    public float anguloGiro = 45f;
    public float velocidadeGiroSuave = 90f;

    [Header("Modo PC (sem oculos)")]
    public float alturaOlhosPC = 1.65f;
    public float sensibilidadeMouse = 0.15f;

    InputAction mover, girar, correr, resetar;
    CharacterController cc;
    Vector3 posInicial;
    Quaternion rotInicial;
    float velY, pitch;
    bool podeGirar = true;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (cabeca == null && Camera.main != null) cabeca = Camera.main.transform;
        posInicial = transform.position;
        rotInicial = transform.rotation;

        mover = new InputAction("Mover", InputActionType.Value, expectedControlType: "Vector2");
        mover.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
        mover.AddBinding("<Gamepad>/leftStick");
        mover.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        mover.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");

        girar = new InputAction("Girar", InputActionType.Value, expectedControlType: "Vector2");
        girar.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
        girar.AddBinding("<Gamepad>/rightStick");
        girar.AddCompositeBinding("2DVector")
            .With("Left", "<Keyboard>/q").With("Right", "<Keyboard>/e");

        correr = new InputAction("Correr", InputActionType.Button);
        correr.AddBinding("<XRController>{LeftHand}/{Primary2DAxisClick}");
        correr.AddBinding("<Gamepad>/leftStickPress");
        correr.AddBinding("<Keyboard>/leftShift");

        resetar = new InputAction("Resetar", InputActionType.Button);
        resetar.AddBinding("<XRController>{RightHand}/{SecondaryButton}");
        resetar.AddBinding("<Gamepad>/buttonNorth");
        resetar.AddBinding("<Keyboard>/r");
    }

    void OnEnable()  { mover.Enable(); girar.Enable(); correr.Enable(); resetar.Enable(); }
    void OnDisable() { mover.Disable(); girar.Disable(); correr.Disable(); resetar.Disable(); }

    void Update()
    {
        if (cabeca == null) return;
        bool modoVR = XRSettings.isDeviceActive;
        float dt = Time.deltaTime;

        if (!modoVR) OlharComMouse();

        AjustarCapsulaNaCabeca();

        // --- Rotacao ---
        float gx = girar.ReadValue<Vector2>().x;
        if (giroSuave)
        {
            if (Mathf.Abs(gx) > 0.1f) Girar(gx * velocidadeGiroSuave * dt);
        }
        else
        {
            if (podeGirar && Mathf.Abs(gx) > 0.7f) { Girar(Mathf.Sign(gx) * anguloGiro); podeGirar = false; }
            else if (Mathf.Abs(gx) < 0.3f) podeGirar = true;
        }

        // --- Movimento (relativo a direcao da cabeca) ---
        Vector2 entrada = Vector2.ClampMagnitude(mover.ReadValue<Vector2>(), 1f);
        Vector3 frente = Vector3.ProjectOnPlane(cabeca.forward, Vector3.up).normalized;
        Vector3 direita = Vector3.ProjectOnPlane(cabeca.right, Vector3.up).normalized;
        float v = correr.IsPressed() ? velocidadeCorrida : velocidade;
        Vector3 deslocamento = (frente * entrada.y + direita * entrada.x) * v;

        if (cc.isGrounded && velY < 0f) velY = -2f;
        velY += gravidade * dt;
        cc.Move((deslocamento + Vector3.up * velY) * dt);

        // --- Reset ---
        if (resetar.WasPressedThisFrame() || transform.position.y < posInicial.y - 50f)
            VoltarAoInicio();
    }

    /// Mantem o colisor (capsula) embaixo da cabeca, para o roomscale funcionar.
    void AjustarCapsulaNaCabeca()
    {
        Vector3 local = transform.InverseTransformPoint(cabeca.position);
        float altura = Mathf.Clamp(local.y + 0.1f, 1.0f, 2.2f);
        cc.height = altura;
        cc.center = new Vector3(local.x, altura / 2f + cc.skinWidth, local.z);
    }

    void Girar(float graus)
    {
        transform.RotateAround(cabeca.position, Vector3.up, graus);
        Physics.SyncTransforms();
    }

    void OlharComMouse()
    {
        cabeca.localPosition = new Vector3(0f, alturaOlhosPC, 0f);
        var mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.isPressed) return;
        Vector2 d = mouse.delta.ReadValue() * sensibilidadeMouse;
        transform.Rotate(0f, d.x, 0f, Space.World);
        pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
        cabeca.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    public void VoltarAoInicio()
    {
        cc.enabled = false;
        transform.SetPositionAndRotation(posInicial, rotInicial);
        cc.enabled = true;
        velY = 0f;
        Physics.SyncTransforms();
    }
}
