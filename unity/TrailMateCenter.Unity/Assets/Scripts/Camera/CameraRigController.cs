using TrailMateCenter.Unity.Bridge;
using TrailMateCenter.Unity.Core;
using UnityEngine;
namespace TrailMateCenter.Unity.CameraSystem
{
public sealed class CameraRigController : MonoBehaviour
{
    [SerializeField] private Camera _camera = null!;
    [SerializeField] private Transform _pivot = null!;
    private BridgeCoordinator? _bridge;

    public void Initialize(BridgeCoordinator bridge, Camera camera, Transform pivot)
    {
        _bridge = bridge;
        _camera = camera;
        _pivot = pivot;
    }

    public void ApplyCameraState(CameraState state)
    {
        if (_camera == null || _pivot == null)
            return;

        _pivot.position = new Vector3(state.X, state.Y, state.Z);
        _pivot.rotation = Quaternion.Euler(state.Pitch, state.Yaw, state.Roll);
        _camera.fieldOfView = state.Fov;

        var payload = BridgeProtocol.CreateCameraStateChanged(state, "camera applied");
        _ = _bridge?.SendAsync(payload);
    }

    public CameraState CaptureState()
    {
        var pos = _pivot.position;
        var rot = _pivot.rotation.eulerAngles;
        return new CameraState
        {
            X = pos.x,
            Y = pos.y,
            Z = pos.z,
            Pitch = rot.x,
            Yaw = rot.y,
            Roll = rot.z,
            Fov = _camera.fieldOfView
        };
    }
}
}

