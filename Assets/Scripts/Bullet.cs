using UnityEngine;

/// <summary>
/// Proyectil genérico disparado por el jugador o por un enemigo.
/// Su comportamiento se configura en tiempo de ejecución mediante Initialize().
///
/// CONFIGURACIÓN DEL PREFAB:
///   - Rigidbody:
///       • Use Gravity    = false
///       • Freeze Rotation: X ✓  Y ✓  Z ✓
///   - Collider (ej. SphereCollider o CapsuleCollider):
///       • Is Trigger = TRUE  ← IMPORTANTE: evita que la bala aplique impulso físico
///   - NO asignar valores en el Inspector para velocidad/daño;
///     esos se pasan por Initialize() al instanciar.
///
/// LÓGICA DE DAÑO:
///   - Bala del jugador (isPlayerBullet = true):  daña a Enemy, ignora a Player
///   - Bala de enemigo (isPlayerBullet = false):  daña a Player, ignora a Enemy
///   - Bala vs bala:                              se ignoran mutuamente
///   - Cualquier otra superficie:                 la bala se destruye
///
/// NOTA UNITY 6:
///   Reemplaza _rb.velocity por _rb.linearVelocity si usas Unity 6+.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Bullet : MonoBehaviour
{
    [Header("Valores por defecto (sobrescritos por Initialize en tiempo de ejecución)")]
    public float velocidad    = 10f;
    public float tiempoDeVida = 3f;
    public float damage       = 10f;

    // Datos de vuelo — asignados por Initialize(), ocultos en el Inspector
    [HideInInspector] public Vector3 direccion;
    [HideInInspector] public bool    isPlayerBullet;

    private Rigidbody _rb;

    // ─── Ciclo de vida ───────────────────────────────────────────────────────
    void Awake()
    {
        _rb             = GetComponent<Rigidbody>();
        _rb.useGravity  = false;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    void Start()
    {
        // Auto-destrucción tras tiempoDeVida segundos
        Destroy(gameObject, tiempoDeVida);

        // Fallback: si Initialize() no fue llamado antes de Start(),
        // aplicar velocidad con los valores del Inspector
        if (_rb.velocity == Vector3.zero && direccion != Vector3.zero)
            _rb.velocity = direccion * velocidad;
    }

    // ─── Inicialización ──────────────────────────────────────────────────────
    /// <summary>
    /// Inicializa la bala inmediatamente después de ser instanciada.
    /// Llamar esto antes del primer Update (funciona correctamente con Instantiate).
    ///
    /// La rotación ya viene configurada desde Weapon.SpawnBala() mediante
    /// Quaternion.LookRotation(direccion), así que el transform visual ya apunta
    /// en la dirección correcta sin trabajo adicional.
    /// </summary>
    /// <param name="dir">Dirección normalizada de viaje.</param>
    /// <param name="speed">Velocidad en unidades/segundo.</param>
    /// <param name="dmg">Cantidad de daño al impactar.</param>
    /// <param name="fromPlayer">true = disparada por el jugador; false = disparada por enemigo.</param>
    public void Initialize(Vector3 dir, float speed, float dmg, bool fromPlayer)
    {
        direccion      = dir.normalized;
        velocidad      = speed;
        damage         = dmg;
        isPlayerBullet = fromPlayer;

        // Aplicar velocidad física — la bala se mueve con el motor de física de Unity
        // Nota Unity 6+: usar _rb.linearVelocity en lugar de _rb.velocity
        _rb.velocity = direccion * velocidad;
    }

    // ─── Colisiones (trigger) ────────────────────────────────────────────────
    // Usamos OnTriggerEnter en lugar de OnCollisionEnter porque:
    //   • El collider de la bala tiene Is Trigger = true
    //   • Los triggers detectan el solapamiento sin aplicar fuerzas físicas
    //   → La bala daña al objetivo pero no lo empuja ni lo desplaza
    //
    // REQUISITO: Player y Enemy deben tener un Collider normal (no trigger)
    // para que OnTriggerEnter se dispare correctamente.
    void OnTriggerEnter(Collider other)
    {
        // ── Ignorar solapamientos con el equipo propio ───────────────────────
        if (isPlayerBullet && other.GetComponent<Player>() != null) return;
        if (!isPlayerBullet && other.GetComponent<Enemy>()  != null) return;
        // Balas no interactúan entre sí
        if (other.GetComponent<Bullet>() != null) return;

        // ── Aplicar daño según origen ────────────────────────────────────────
        if (isPlayerBullet)
            other.GetComponent<Enemy>()?.TakeDamage(damage);
        else
            other.GetComponent<Player>()?.TakeDamage(damage);

        Destroy(gameObject);
    }
}