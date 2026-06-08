using UnityEngine;

/// <summary>
/// Singleton que mantiene el estado global del juego.
/// Coloca este componente en un GameObject en la escena inicial.
/// 
/// USO:
///   GameState.Instance.equippedWeaponKey = "ShotgunWeapon";
///   GameState.Instance.SumarPuntos(100);
/// </summary>
public class GameState : MonoBehaviour
{
    public static GameState Instance { get; private set; }

    [Header("Arma equipada")]
    [Tooltip("Clave Addressable del arma actualmente equipada. " +
             "Debe coincidir con el nombre del Asset en el grupo de Addressables.")]
    public string equippedWeaponKey = "BasicWeapon";

    [Header("Datos del jugador")]
    public string playerName = "Jugador1";
    public int score         = 0;
    public int level         = 1;

    // ─── Ciclo de vida ───────────────────────────────────────────────────────
    void Awake()
    {
        // Patrón singleton: si ya existe una instancia, destruir el duplicado
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); // Persiste entre escenas
    }

    // ─── Métodos públicos ────────────────────────────────────────────────────
    /// <summary>Suma puntos al marcador global.</summary>
    public void SumarPuntos(int puntos) => score += puntos;

    /// <summary>
    /// Cambia el arma equipada. Después de llamar esto, llama a
    /// Player.EquiparArmaDesdeGameState() para aplicar el cambio en juego.
    /// </summary>
    public void CambiarArma(string nuevaClave)
    {
        equippedWeaponKey = nuevaClave;
        Debug.Log($"[GameState] Arma cambiada a: {nuevaClave}");
    }
}