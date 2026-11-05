// Wrapped so this VRChat/Udon script is ignored in projects without the VRChat SDK.
#if VRC_SDK_VRCSDK3
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class crayon : UdonSharpBehaviour
{
    [SerializeField] private TrailRenderer trail;

    // Triggers when you squeeze the trigger while holding the object
    public override void OnPickupUseDown()
    {
        if (trail != null)
        {
            trail.emitting = true; // Starts drawing
        }
    }

    // Triggers when you release the trigger
    public override void OnPickupUseUp()
    {
        if (trail != null)
        {
            trail.emitting = false; // Stops drawing
        }
    }

    // Optional: Turns off the trail if you drop the marker while drawing
    public override void OnDrop()
    {
        if (trail != null)
        {
            trail.emitting = false;
        }
    }
}
#endif
