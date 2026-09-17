using UnityEngine;

[DisallowMultipleComponent]
public sealed class SpiderBotFootContact : MonoBehaviour
{
    private int _contacts;
    public bool IsGrounded => _contacts > 0;

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.collider.transform.IsChildOf(transform.root)) _contacts++;
    }

    private void OnCollisionExit(Collision collision)
    {
        if (!collision.collider.transform.IsChildOf(transform.root))
            _contacts = Mathf.Max(0, _contacts - 1);
    }

    private void OnDisable() => _contacts = 0;
}
