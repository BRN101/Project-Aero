using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.AddressableAssets;

/// <summary>
/// Controlador principal del jugador.
///
/// SISTEMA DE MOVIMIENTO:
///   - Eje X: desplazamiento horizontal
///   - Eje Y: desplazamiento vertical (el personaje flota/vuela)
///   - Eje Z: profundidad (adelante/atrás), no controlado por el jugador
///
/// SISTEMA DE ARMA:
///   - Lee equippedWeaponKey desde GameState
///   - Instancia el prefab via Addressables como hijo (sin asignar en el Inspector)
///   - Llama a EquiparArmaDesdeGameState() para cambiar arma en tiempo de ejecución
///
/// REQUISITOS EN EL INSPECTOR:
///   - Componente PlayerInput con acciones: Move (Vector2), Shoot (Button), Aim (Vector2)
///   - Rigidbody: Use Gravity = false, Freeze Rotation XYZ
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerInput))]
public class Player : MonoBehaviour
{
    [Header("Movimiento")]
    public float speed = 10f;

    [Header("Vida")]
    public float maxHealth = 100f;

    [Header("Posición del arma (offset local respecto al jugador)")]
    [Tooltip("Ajusta este vector para cambiar dónde aparece el arma. " +
             "Ejemplo: (0.5, 0, 0.5) → derecha del jugador, ligeramente adelante.")]
    public Vector3 weaponOffset = new Vector3(0.5f, 0f, 0.5f);

    // ─── Estado interno ──────────────────────────────────────────────────────
    private float   _currentHealth;
    private Weapon  _currentWeapon;
    private Rigidbody _rb;

    // ─── Input ───────────────────────────────────────────────────────────────
    private PlayerInput _playerInput;
    private InputAction _moveAction;
    private InputAction _shootAction;
    private InputAction _aimAction;

    // ─── Ciclo de vida ───────────────────────────────────────────────────────
    void Awake()
    {
        _rb             = GetComponent<Rigidbody>();
        _playerInput    = GetComponent<PlayerInput>();
        _currentHealth  = maxHealth;

        // Configurar física: sin gravedad, sin rotación por física
        _rb.useGravity  = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    void Start()
    {
        if (_playerInput != null)
        {
            _moveAction  = _playerInput.actions["Move"];
            _shootAction = _playerInput.actions["Shoot"];
            _aimAction   = _playerInput.actions["Aim"];
        }
        else
            Debug.LogError("[Player] No se encontró PlayerInput en " + gameObject.name);

        EquiparArmaDesdeGameState();
    }

    void Update()
    {
        // Disparar al presionar el botón de disparo
        if (_shootAction != null && _shootAction.IsPressed())
        {
            if (_currentWeapon != null && _aimAction != null)
                _currentWeapon.Shoot(_aimAction.ReadValue<Vector2>());
        }
    }

    void FixedUpdate()
    {
        if (_moveAction == null) return;

        Vector2 input = _moveAction.ReadValue<Vector2>();
        // Movimiento en plano XY; Z permanece en 0 (el jugador no se mueve en profundidad)
        // Nota Unity 6: reemplazar _rb.velocity por _rb.linearVelocity
        _rb.velocity = new Vector3(input.x * speed, input.y * speed, 0f);
    }

    // ─── Sistema de arma ─────────────────────────────────────────────────────
    /// <summary>
    /// Lee la clave del arma equipada desde GameState e instancia el prefab
    /// como hijo del jugador usando Addressables. No requiere asignación manual en el Inspector.
    ///
    /// Llama a este método también cuando el jugador cambie de arma:
    ///   GameState.Instance.CambiarArma("ShotgunWeapon");
    ///   GetComponent&lt;Player&gt;().EquiparArmaDesdeGameState();
    /// </summary>
    public void EquiparArmaDesdeGameState()
    {
        if (GameState.Instance == null)
        {
            Debug.LogError("[Player] GameState no encontrado. " +
                            "Asegúrate de tener un GameObject con el componente GameState en la escena.");
            return;
        }

        string clave = GameState.Instance.equippedWeaponKey;
        Debug.Log($"[Player] Equipando arma: '{clave}'");

        Addressables.InstantiateAsync(clave, transform).Completed += handle =>
        {
            if (handle.Result == null)
            {
                Debug.LogError($"[Player] No se pudo instanciar '{clave}' desde Addressables. " +
                                "Verifica que la clave existe en el grupo de Addressables.");
                return;
            }

            // Liberar el arma anterior correctamente (evita memory leak con Addressables)
            if (_currentWeapon != null)
                Addressables.ReleaseInstance(_currentWeapon.gameObject);

            // Posicionar el arma como hijo con el offset configurado
            Transform armaT = handle.Result.transform;
            armaT.SetParent(transform, worldPositionStays: false);
            armaT.localPosition = weaponOffset;
            armaT.localRotation = Quaternion.identity;

            _currentWeapon = handle.Result.GetComponent<Weapon>();

            if (_currentWeapon == null)
                Debug.LogError($"[Player] El prefab '{clave}' no tiene el componente Weapon adjunto.");
            else
                Debug.Log($"[Player] Arma '{clave}' equipada correctamente.");
        };
    }

    // ─── Sistema de vida ─────────────────────────────────────────────────────
    public void TakeDamage(float cantidad)
    {
        _currentHealth -= cantidad;
        Debug.Log($"[Player] Recibió {cantidad} daño. Vida: {_currentHealth}/{maxHealth}");

        if (_currentHealth <= 0f)
            Morir();
    }

    void Morir()
    {
        Debug.Log("[Player] Ha muerto.");
        gameObject.SetActive(false);
        // Aquí: disparar evento de Game Over, animación de muerte, etc.
    }

    // ─── Propiedades de solo lectura ─────────────────────────────────────────
    public float HealthActual => _currentHealth;
    public float HealthMax    => maxHealth;
    public bool  EstaVivo     => _currentHealth > 0f;
}