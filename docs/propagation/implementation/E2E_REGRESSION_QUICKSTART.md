# E2E_REGRESSION_QUICKSTART

This quickstart validates the desktop propagation workflow with:

- mock simulation service (in app)
- real Unity bridge transport
- Unity stub server (named pipe or TCP)

## 1. Prerequisites

- .NET SDK installed
- repository restored and buildable
- Windows PowerShell can run scripts (`ExecutionPolicy Bypass` for command session)

## 2. One-command run

```powershell
pwsh ./scripts/propagation/run_e2e_regression.ps1 -Configuration Debug -UseUnityStub
```

What it does:

1. starts `TrailMateCenter.Propagation.UnityStubServer`
2. starts desktop app (`TrailMateCenter.App`)
3. configures app environment for:
   - mock simulation service
   - real Unity bridge
   - external Unity runtime mode

## 3. Environment switches used by script

- `TRAILMATE_PROPAGATION_SIMULATION_USE_MOCK=true`
- `TRAILMATE_PROPAGATION_UNITY_BRIDGE_USE_MOCK=false`
- `TRAILMATE_PROPAGATION_UNITY_PROCESS_USE_MOCK=true`
- `TRAILMATE_PROPAGATION_UNITY_AUTO_ATTACH=true`

## 4. Expected checks in UI

1. open Propagation tab
2. Unity bridge state becomes attached
3. click `Run`
4. observe layer state updates and diagnostics
5. observe interaction telemetry updates after map actions

## 5. Logs

Logs are in `artifacts/propagation-regression`:

- `unity_stub_*.out.log`
- `unity_stub_*.err.log`
- `app_*.out.log`
- `app_*.err.log`

## 6. Unity Play Smoke (Editor)

If Unity Editor is installed, run:

```powershell
pwsh ./scripts/propagation/run_unity_editor_play_smoke.ps1 `
  -UnityExePath "C:\\Program Files\\Unity\\Hub\\Editor\\2022.3.20f1\\Editor\\Unity.exe" `
  -DurationSeconds 10
```

This command enters play mode once in batchmode and exits with non-zero code on compile/runtime log errors.
