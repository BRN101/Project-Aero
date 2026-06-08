using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Clase base para todas las armas del jugador.
/// Instancia balas desde un firePoint y apunta hacia la posición del cursor en pantalla.
///
/// CÓMO CREAR UNA NUEVA ARMA (ejemplo: triple disparo):
///
///   public class ArmaTriple : Weapon
///   {
///       public float anguloAbanico = 15f;
///
///       public override void Shoot(Vector2 screenPosition)
///       {
///           if (!PuedoDisparar()) return;
///           _lastFireTime = Time.time;
///
///           Vector3 origen    = ObtenerOrigenDisparo();
///           Vector3 objetivo  = PantallaMundo(screenPosition, origen);
///           Vector3 dirCentro = (objetivo - origen).normalized;
///
///           // Disparo central
///           LanzarBala(origen, dirCentro);
///           // Disparos laterales
///           LanzarBala(origen, Quaternion.Euler(0, -anguloAbanico, 0) * dirCentro);
///           LanzarBala(origen, Quaternion.Euler(0,  anguloAbanico, 0) * dirCentro);
///       }
///
///       void LanzarBala(Vector3 origen, Vector3 dir)
///       {
///           Quaternion rot = dir != Vector3.zero ? Quaternion.LookRotation(dir) : Quaternion.identity;
///           SpawnBala(origen, dir, rot, isPlayerBullet: true);
///       }
///   }
///
/// CONFIGURACIÓN DEL PREFAB:
///   - Componente Weapon (o la subclase) adjunto al root
///   - Hijo vacío "FirePoint" posicionado en la punta del cañón
///   - bulletPrefab asignado en Inspector, o bulletAddressableKey configurado
/// </summary>
public class Weapon : MonoBehaviour
{
    [Header("Prefab de la bala")]
    [Tooltip("Arrastra el prefab de la bala aquí. " +
            "Si no se asigna, se usará bulletAddressableKey para cargarlo.")]
    public GameObject bulletPrefab;

    [Tooltip("Clave Addressable de la bala. Solo se usa si bulletPrefab está vacío.")]
    public string bulletAddressableKey = "";

    [Header("Parámetros de disparo")]
    public float bulletSpeed = 15f;
    public float damage      = 10f;
    [Tooltip("Tiempo mínimo en segundos entre disparos.")]
    public float fireRate    = 0.25f;

    [Header("Punto de salida de la bala")]
    [Tooltip("Transform hijo del arma desde donde emergen las balas. " +
            "Si se deja vacío, se usa la posición del arma. " +
            "Crea un GameObject vacío hijo llamado 'FirePoint' y asígnalo aquí.")]
    public Transform firePoint;

    // Timestamp del último disparo (Mathf.NegativeInfinity permite disparar desde el primer frame)
    protected float _lastFireTime = Mathf.NegativeInfinity;

    // ─── Ciclo de vida ───────────────────────────────────────────────────────
    /// <summary>
    /// Si sobreescribes Start() en una subclase, llama base.Start()
    /// para que la carga de Addressables funcione correctamente.
    /// </summary>
    protected virtual void Start()
    {
        if (bulletPrefab == null && !string.IsNullOrEmpty(bulletAddressableKey))
        {
            Addressables.LoadAssetAsync<GameObject>(bulletAddressableKey).Completed += handle =>
            {
                if (handle.Result != null)
                    bulletPrefab = handle.Result;
                else
                    Debug.LogError($"[Weapon] No se pudo cargar bala con clave '{bulletAddressableKey}'.");
            };
        }
    }

    // ─── Disparo principal ───────────────────────────────────────────────────
    /// <summary>
    /// Dispara un proyectil hacia la posición del cursor en pantalla.
    ///
    /// Sobreescribe este método en subclases para implementar otros patrones:
    ///   - Triple disparo / disparo en abanico
    ///   - Proyectiles con seguimiento (homing)
    ///   - Ráfagas con retardo (StartCoroutine)
    ///   - Disparos cargados
    ///
    /// IMPORTANTE: Gestiona _lastFireTime y llama a PuedoDisparar() tú mismo
    /// si sobreescribes completamente este método.
    /// </summary>
    /// <param name="screenPosition">Posición del cursor/mira en coordenadas de pantalla.</param>
    public virtual void Shoot(Vector2 screenPosition)
    {
        if (!PuedoDisparar()) return;
        _lastFireTime = Time.time;

        Vector3 origen    = ObtenerOrigenDisparo();
        Vector3 objetivo  = PantallaMundo(screenPosition, origen);
        Vector3 direccion = (objetivo - origen).normalized;
        Quaternion rotacion = direccion != Vector3.zero
            ? Quaternion.LookRotation(direccion)
            : Quaternion.identity;

        SpawnBala(origen, direccion, rotacion, isPlayerBullet: true);
    }

    // ─── Helpers protegidos (disponibles para subclases) ─────────────────────
    /// <summary>
    /// Instancia una bala en la posición y dirección dadas.
    /// Usa este helper dentro de las sobreescrituras de Shoot().
    /// </summary>
    protected void SpawnBala(Vector3 origen, Vector3 direccion, Quaternion rotacion, bool isPlayerBullet)
    {
        if (bulletPrefab == null)
        {
            Debug.LogWarning($"[{gameObject.name}] bulletPrefab no asignado. " +
                            "Asigna el prefab en el Inspector o configura bulletAddressableKey.");
            return;
        }

        GameObject balaObj = Instantiate(bulletPrefab, origen, rotacion);
        Bullet bala = balaObj.GetComponent<Bullet>();

        if (bala != null)
            bala.Initialize(direccion, bulletSpeed, damage, isPlayerBullet);
        else
            Debug.LogWarning($"[{gameObject.name}] El prefab de bala no tiene el componente Bullet.");
    }

    /// <summary>
    /// Retorna true si ha pasado suficiente tiempo desde el último disparo.
    /// Llama esto al inicio de cualquier sobreescritura de Shoot().
    /// </summary>
    protected bool PuedoDisparar() => Time.time >= _lastFireTime + fireRate;

    /// <summary>
    /// Devuelve el punto desde donde sale la bala.
    /// Prioriza firePoint si está asignado; si no, usa la posición del arma.
    /// </summary>
    protected Vector3 ObtenerOrigenDisparo() =>
        firePoint != null ? firePoint.position : transform.position;

    /// <summary>
    /// Convierte coordenadas de pantalla (cursor) a coordenadas del mundo 3D,
    /// proyectadas al mismo plano Z que el punto de origen del disparo.
    ///
    /// Esto garantiza que la dirección de la bala sea correcta en un entorno
    /// donde el movimiento del jugador es en el plano XY y Z es la profundidad.
    /// </summary>
    /// <param name="screenPos">Posición del cursor en píxeles de pantalla.</param>
    /// <param name="referenciaPos">Posición 3D usada para calcular la profundidad Z.</param>
    protected Vector3 PantallaMundo(Vector2 screenPos, Vector3 referenciaPos)
    {
        float distZ = Mathf.Abs(Camera.main.transform.position.z - referenciaPos.z) + 8f;
        return Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, distZ));
    }
}