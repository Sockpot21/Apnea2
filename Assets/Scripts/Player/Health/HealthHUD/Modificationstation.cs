// ModificationStation.cs
// Attach to your station world GameObject.
//
// ?? Scene setup you need to do ???????????????????????????????????????????????
// 1. Create a GameObject in the scene for the station (e.g. a cube or your model).
//    Name it "ModificationStation".
// 2. Attach this script to it.
// 3. Make sure it has a Collider (any type). It does NOT need to be a trigger.
// 4. Assign the "Station Ui" field to your ModificationStationUI component
//    (we'll create that GameObject on the Canvas next).
// 5. Set "Interaction Distance" to however far the player can be to interact (e.g. 2.5).
// 6. Optionally assign a "Prompt Label" — a world-space or screen-space TMP text
//    that shows "Press E to modify" when the player is close enough.
// ?????????????????????????????????????????????????????????????????????????????

using UnityEngine;
using TMPro;

public class ModificationStation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ModificationStationUI stationUI;

    [Header("Interaction")]
    [SerializeField] private float interactionDistance = 2.5f;

    [Header("Prompt (optional — assign a world-space TMP text if you want one)")]
    [SerializeField] private TextMeshPro promptLabel;

    private Transform _playerTransform;
    private bool _playerInRange = false;

    // ?? Lifecycle ?????????????????????????????????????????????????????????????

    private void Start()
    {
        // Find player by tag — make sure your player GameObject is tagged "Player"
        var playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
            _playerTransform = playerGO.transform;
        else
            Debug.LogWarning("[ModificationStation] No GameObject tagged 'Player' found.");

        if (promptLabel != null)
            promptLabel.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (_playerTransform == null) return;

        float dist = Vector3.Distance(transform.position, _playerTransform.position);
        bool inRange = dist <= interactionDistance;

        if (inRange != _playerInRange)
        {
            _playerInRange = inRange;
            if (promptLabel != null)
                promptLabel.gameObject.SetActive(_playerInRange);
        }
    }

    // ?? Called by PlayerInteraction when E is pressed ?????????????????????????

    /// <summary>
    /// Returns true if the player is within interaction distance of this station.
    /// Called by PlayerInteraction before opening the UI.
    /// </summary>
    public bool IsPlayerInRange() => _playerInRange;

    /// <summary>
    /// Opens the modification UI. Called by PlayerInteraction.
    /// </summary>
    public void Interact()
    {
        stationUI?.Open();
    }
}
