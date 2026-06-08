using System.Collections;
using UnityEngine;

// ─── Patrones de movimiento disponibles ─────────────────────────────────────
public enum TipoMovimiento
{
    Estatico,            // Sin movimiento; permanece en idlePosition
    HorizontalOscilante, // Vaivén izquierda/derecha con función seno
    VerticalOscilante,   // Vaivén arriba/abajo con función seno
    Diagonal,            // Vaivén en la dirección definida por direccionDiagonal
    Circular,            // Órbita alrededor de idlePosition
    Figura8,             // Curva de Lissajous — traza un infinito (∞)
    Ola,                 // Movimiento de pulpo: impulso asimétrico y deslizamiento
    Waypoints            // Recorre puntos predefinidos en bucle
}

public class Enemy : MonoBehaviour
{
    // ─── VIDA ────────────────────────────────────────────────────────────────
    [Header("Vida")]
    public float maxHealth = 30f;
    protected float _currentHealth;

    // ─── DISPARO ─────────────────────────────────────────────────────────────
    [Header("Disparo")]
    public GameObject bulletPrefab;
    public float bulletSpeed = 8f;
    public float damage      = 10f;
    public float fireRate    = 2f;
    [Tooltip("Distancia máxima al jugador para disparar.")]
    public float shootRange  = 20f;

    // ─── ACTIVACIÓN ──────────────────────────────────────────────────────────
    [Header("Activación")]
    [Tooltip("Distancia desde la idlePosition hasta el jugador a la que el enemigo se activa.")]
    public float activationRange = 35f;

    // ─── TWEEN DE ENTRADA ────────────────────────────────────────────────────
    [Header("Tween de entrada")]
    [Tooltip("Offset desde idlePosition donde spawna el enemigo (fuera de cámara).")]
    public Vector3 entryOffset  = new Vector3(0f, -20f, 0f);
    [Tooltip("Duración del tween de entrada en segundos.")]
    public float entryDuration  = 1.5f;
    [Tooltip("Curva de animación del tween.")]
    public AnimationCurve entryCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ─── POSICIÓN IDLE ────────────────────────────────────────────────────────
    [Header("Posición idle")]
    [Tooltip("Centro del patrón de movimiento en coordenadas del mundo.")]
    public Vector3 idlePosition = Vector3.zero;

    // ─── MOVIMIENTO IDLE ──────────────────────────────────────────────────────
    [Header("Patrón de movimiento idle")]
    public TipoMovimiento tipoMovimiento = TipoMovimiento.HorizontalOscilante;

    [Tooltip("Frecuencia del ciclo: ciclos por segundo.")]
    public float velocidadMovimiento = 0.4f;

    [Tooltip("Amplitud / radio del movimiento en unidades de mundo.")]
    public float amplitud = 3f;
    
    [Tooltip("Duración en segundos para suavizar el inicio del movimiento (0 a máxima amplitud).")]
    public float blendDuration = 1.5f;

    [Tooltip("[Solo Diagonal] Dirección del vaivén.")]
    public Vector2 direccionDiagonal = new Vector2(1f, 0.5f);

    [Tooltip("[Solo Circular] Radio de la órbita.")]
    public float radioCircular = 3f;

    [Tooltip("[Solo Waypoints] Posiciones en espacio LOCAL relativas a idlePosition.")]
    public Vector3[] waypoints;

    [Tooltip("[Solo Waypoints] Velocidad de movimiento entre waypoints.")]
    public float velocidadWaypoints = 3f;

    // ─── SALIDA DE ESCENA ─────────────────────────────────────────────────────
    [Header("Salida de escena")]
    [Tooltip("Segundos en estado Idle antes de iniciar la salida.")]
    public float tiempoEnEscena    = 8f;
    [Tooltip("Duración del tween de salida en segundos.")]
    public float exitDuration      = 1.5f;
    [Tooltip("Qué tan lejos sale el enemigo: distancia = entryOffset.magnitude × este valor.")]
    public float exitDistanceMult  = 1.5f;

    // ─── ESTADO INTERNO ───────────────────────────────────────────────────────
    protected enum EnemyState { Dormant, Entering, Idle, Exiting, Dead }
    protected EnemyState _state = EnemyState.Dormant;

    protected float     _lastFireTime = Mathf.NegativeInfinity;
    protected Transform _playerTransform;

    private Vector3    _idlePos;           
    private float      _movTime  = 0f;     
    private float      _amplitudeBlend = 0f; // Controla la suavidad (0 a 1)
    private Vector3    _prevPos;           
    private Vector3    _exitDirection;     
    private int        _waypointIndex = 0;
    private Renderer[] _renderers;
    private Collider[] _colliders;

    // ─── CICLO DE VIDA ────────────────────────────────────────────────────────
    protected virtual void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        _colliders = GetComponentsInChildren<Collider>(includeInactive: true);
    }

    protected virtual void Start()
    {
        _currentHealth = maxHealth;
        _idlePos = (idlePosition != Vector3.zero) ? idlePosition : transform.position;

        transform.position = _idlePos + entryOffset;
        _prevPos           = transform.position;
        _exitDirection     = -entryOffset.normalized;

        SetDormant(true);
        BuscarJugador();
    }

    protected virtual void Update()
    {
        if (_playerTransform == null) { BuscarJugador(); return; }

        switch (_state)
        {
            case EnemyState.Dormant:
                VerificarActivacion();
                break;
            case EnemyState.Idle:
                ActualizarMovimientoIdle();
                VerificarDisparo();
                break;
        }
    }

    // ─── ACTIVACIÓN ──────────────────────────────────────────────────────────
    void VerificarActivacion()
    {
        float dist = Vector3.Distance(_idlePos, _playerTransform.position);
        if (dist <= activationRange) Activar();
    }

    void Activar()
    {
        SetDormant(false);
        _state = EnemyState.Entering;
        StartCoroutine(TweenEntrada());
    }

    void SetDormant(bool dormant)
    {
        foreach (var r in _renderers) r.enabled  = !dormant;
        foreach (var c in _colliders) c.enabled  = !dormant;
    }

    // ─── TWEEN DE ENTRADA ─────────────────────────────────────────────────────
    IEnumerator TweenEntrada()
    {
        Vector3 startPos = transform.position; 
        Vector3 endPos   = _idlePos;
        float   elapsed  = 0f;

        while (elapsed < entryDuration)
        {
            elapsed += Time.deltaTime;
            float t      = Mathf.Clamp01(elapsed / entryDuration);
            float curveT = entryCurve.Evaluate(t);
            transform.position = Vector3.LerpUnclamped(startPos, endPos, curveT);
            yield return null;
        }

        transform.position = endPos;
        _movTime           = 0f;
        _amplitudeBlend    = 0f; // Inicia la mezcla de amplitud para el patrón
        _prevPos           = transform.position;
        _state             = EnemyState.Idle;

        StartCoroutine(TemporizadorSalida());
    }

    // ─── MOVIMIENTO IDLE ──────────────────────────────────────────────────────
    void ActualizarMovimientoIdle()
    {
        _prevPos  = transform.position;
        _movTime += Time.deltaTime;

        Vector3 nuevaPos;

        if (tipoMovimiento == TipoMovimiento.Waypoints && waypoints != null && waypoints.Length > 0)
        {
            nuevaPos = ActualizarWaypoints();
        }
        else
        {
            // Suaviza la entrada al patrón de movimiento orgánicamente
            _amplitudeBlend = Mathf.Clamp01(_movTime / Mathf.Max(0.01f, blendDuration));
            nuevaPos = _idlePos + CalcularOffset(_movTime) * _amplitudeBlend;
        }

        transform.position = nuevaPos;

        Vector3 delta = transform.position - _prevPos;
        if (delta.sqrMagnitude > 0.00001f)
            _exitDirection = delta.normalized;
    }

    Vector3 CalcularOffset(float t)
    {
        float ang = t * velocidadMovimiento * Mathf.PI * 2f;

        switch (tipoMovimiento)
        {
            case TipoMovimiento.Estatico:
                return Vector3.zero;
            case TipoMovimiento.HorizontalOscilante:
                return new Vector3(Mathf.Sin(ang) * amplitud, 0f, 0f);
            case TipoMovimiento.VerticalOscilante:
                return new Vector3(0f, Mathf.Sin(ang) * amplitud, 0f);
            case TipoMovimiento.Diagonal:
                Vector2 dir = direccionDiagonal.normalized;
                float   d   = Mathf.Sin(ang) * amplitud;
                return new Vector3(dir.x * d, dir.y * d, 0f);
            case TipoMovimiento.Circular:
                return new Vector3(Mathf.Cos(ang) * radioCircular, Mathf.Sin(ang) * radioCircular, 0f);
            case TipoMovimiento.Figura8:
                return new Vector3(Mathf.Sin(ang) * amplitud, Mathf.Sin(ang * 2f) * amplitud * 0.5f, 0f);
            
            case TipoMovimiento.Ola:
                // Efecto Pulpo: Usamos un desfase asimétrico (restando un coseno al ángulo del seno)
                // Esto genera un "jalón" brusco hacia un lado y un deslizamiento suave de regreso.
                float asimetria = 1.2f;
                float y = Mathf.Sin(ang - asimetria * Mathf.Cos(ang)) * amplitud;
                // Pequeño bamboleo secundario
                float x = Mathf.Sin(ang * 0.5f) * amplitud * 0.2f;
                return new Vector3(x, y, 0f);

            default: return Vector3.zero;
        }
    }

    Vector3 ActualizarWaypoints()
    {
        Vector3 destino  = _idlePos + waypoints[_waypointIndex];
        float   step     = velocidadWaypoints * Time.deltaTime;
        Vector3 nuevaPos = Vector3.MoveTowards(transform.position, destino, step);

        if (Vector3.Distance(nuevaPos, destino) < 0.05f)
            _waypointIndex = (_waypointIndex + 1) % waypoints.Length;

        return nuevaPos;
    }

    // ─── SALIDA DE ESCENA ─────────────────────────────────────────────────────
    IEnumerator TemporizadorSalida()
    {
        yield return new WaitForSeconds(tiempoEnEscena);
        if (_state == EnemyState.Idle) StartCoroutine(TweenSalida());
    }

    IEnumerator TweenSalida()
    {
        _state = EnemyState.Exiting;

        Vector3 startIdle = _idlePos;
        Vector3 dirSalida = (_exitDirection.sqrMagnitude > 0.001f) ? _exitDirection : -entryOffset.normalized;
        Vector3 endIdle   = startIdle + dirSalida * (entryOffset.magnitude * exitDistanceMult);
        
        float   elapsed      = 0f;
        Vector3 startRealPos = transform.position;

        while (elapsed < exitDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / exitDuration);
            // Curva de aceleración cúbica para empezar con muy poca resistencia
            float easeIn = t * t * t; 

            if (tipoMovimiento == TipoMovimiento.Waypoints)
            {
                // Si son waypoints, hacemos lerp directo desde su posición actual
                transform.position = Vector3.LerpUnclamped(startRealPos, endIdle, easeIn);
            }
            else
            {
                // Para movimientos matemáticos, movemos el "centro" hacia afuera...
                _idlePos = Vector3.LerpUnclamped(startIdle, endIdle, easeIn);
                
                // ...y dejamos que la oscilación continúe pero se desvanezca
                _movTime += Time.deltaTime;
                _amplitudeBlend = 1f - t; 
                transform.position = _idlePos + CalcularOffset(_movTime) * _amplitudeBlend;
            }
            yield return null;
        }
        Morir();
    }

    // ─── DISPARO ─────────────────────────────────────────────────────────────
    void VerificarDisparo()
    {
        if (_playerTransform == null) return;
        float dist = Vector3.Distance(transform.position, _playerTransform.position);
        if (dist <= shootRange && Time.time >= _lastFireTime + fireRate)
            Shoot();
    }

    public virtual void Shoot()
    {
        if (bulletPrefab == null || _playerTransform == null) return;
        _lastFireTime = Time.time;

        Vector3    dir = (_playerTransform.position - transform.position).normalized;
        Quaternion rot = dir != Vector3.zero ? Quaternion.LookRotation(dir) : Quaternion.identity;
        SpawnBalaEnemigo(transform.position, dir, rot);
    }

    protected void SpawnBalaEnemigo(Vector3 origen, Vector3 dir, Quaternion rot)
    {
        if (bulletPrefab == null) return;
        GameObject balaObj = Instantiate(bulletPrefab, origen, rot);
        Bullet     bala    = balaObj.GetComponent<Bullet>();
        bala?.Initialize(dir, bulletSpeed, damage, fromPlayer: false);
    }

    // ─── VIDA ─────────────────────────────────────────────────────────────────
    public virtual void TakeDamage(float cantidad)
    {
        _currentHealth -= cantidad;
        if (_currentHealth <= 0f) Morir();
    }

    protected virtual void Morir()
    {
        if (_state == EnemyState.Dead) return;
        _state = EnemyState.Dead;
        StopAllCoroutines();
        Destroy(gameObject);
    }

    // ─── UTILIDADES ───────────────────────────────────────────────────────────
    void BuscarJugador()
    {
        Player p = FindObjectOfType<Player>();
        if (p != null) _playerTransform = p.transform;
    }

    // ─── GIZMOS ──────────────────────────────────────────────────────────────
    void OnDrawGizmosSelected()
    {
        Vector3 pos = Application.isPlaying ? _idlePos : (idlePosition != Vector3.zero ? idlePosition : transform.position);

        Gizmos.color = new Color(1f, 0.9f, 0f, 0.4f);
        Gizmos.DrawWireSphere(pos, activationRange);

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(pos, shootRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(pos, 0.25f);

        Gizmos.color = Color.green;
        Vector3 entryPos = pos + entryOffset;
        Gizmos.DrawSphere(entryPos, 0.25f);
        Gizmos.DrawLine(entryPos, pos);

        if (tipoMovimiento == TipoMovimiento.Waypoints && waypoints != null && waypoints.Length > 0)
        {
            Gizmos.color = Color.magenta;
            for (int i = 0; i < waypoints.Length; i++)
            {
                Vector3 wp   = pos + waypoints[i];
                Vector3 next = pos + waypoints[(i + 1) % waypoints.Length];
                Gizmos.DrawSphere(wp, 0.18f);
                Gizmos.DrawLine(wp, next);
            }
        }

        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        if (tipoMovimiento != TipoMovimiento.Waypoints && tipoMovimiento != TipoMovimiento.Estatico)
        {
            int     steps    = 64;
            Vector3 prevPt   = pos + CalcularOffset(0f);
            for (int i = 1; i <= steps; i++)
            {
                float   t    = (float)i / steps * (1f / Mathf.Max(velocidadMovimiento, 0.01f));
                Vector3 pt   = pos + CalcularOffset(t);
                Gizmos.DrawLine(prevPt, pt);
                prevPt = pt;
            }
        }
    }
}