using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace UnityBridge
{
    /// <summary>
    /// Touch input simulation engine using the new Input System's virtual Touchscreen device.
    /// Processes frame-based touch sequences queued from the HTTP thread.
    /// </summary>
    public static class InputEmulator
    {
        private static Touchscreen virtualTouchscreen;
        private static bool initialized;

        private static readonly object pendingLock = new object();
        private static readonly List<InputSequence> pendingSequences = new List<InputSequence>();
        private static readonly List<InputSequence> activeSequences = new List<InputSequence>();
        private static readonly List<CompletedInput> completedInputs = new List<CompletedInput>();
        private static int nextInputId = 1;

        // --- Public data classes ---

        [Serializable]
        public class InputStatusData
        {
            public int activeCount;
            public int completedCount;
            public ActiveInputInfo[] active;
            public CompletedInput[] recentCompleted;
        }

        [Serializable]
        public class ActiveInputInfo
        {
            public string inputId;
            public string action;
            public float progress;
        }

        [Serializable]
        public class CompletedInput
        {
            public string inputId;
            public string action;
            public string completedAt;
        }

        // --- Sequence hierarchy ---

        public abstract class InputSequence
        {
            public string InputId { get; set; }
            public string Action { get; set; }
            public float Duration { get; set; }
            public float Elapsed { get; set; }
            public bool IsComplete { get; set; }
            public float EstimatedDuration => Duration;
            public float Progress => Duration > 0 ? Mathf.Clamp01(Elapsed / Duration) : (IsComplete ? 1f : 0f);

            public abstract void Update(Touchscreen device, float deltaTime);
        }

        public class TapSequence : InputSequence
        {
            public float X, Y;
            private int phase; // 0=began, 1=stationary, 2=ended, 3=done

            public TapSequence(float x, float y, float duration = 0.05f)
            {
                X = x; Y = y; Duration = duration; Action = "tap";
            }

            public override void Update(Touchscreen device, float deltaTime)
            {
                Elapsed += deltaTime;
                switch (phase)
                {
                    case 0:
                        QueueTouch(device, 0, X, Y, UnityEngine.InputSystem.TouchPhase.Began);
                        phase = 1;
                        break;
                    case 1:
                        if (Elapsed >= Duration)
                        {
                            QueueTouch(device, 0, X, Y, UnityEngine.InputSystem.TouchPhase.Ended);
                            phase = 2;
                        }
                        else
                        {
                            QueueTouch(device, 0, X, Y, UnityEngine.InputSystem.TouchPhase.Stationary);
                        }
                        break;
                    case 2:
                        IsComplete = true;
                        break;
                }
            }
        }

        public class HoldSequence : InputSequence
        {
            public float X, Y;
            private bool began;

            public HoldSequence(float x, float y, float duration = 1.0f)
            {
                X = x; Y = y; Duration = duration; Action = "hold";
            }

            public override void Update(Touchscreen device, float deltaTime)
            {
                Elapsed += deltaTime;
                if (!began)
                {
                    QueueTouch(device, 0, X, Y, UnityEngine.InputSystem.TouchPhase.Began);
                    began = true;
                }
                else if (Elapsed >= Duration)
                {
                    QueueTouch(device, 0, X, Y, UnityEngine.InputSystem.TouchPhase.Ended);
                    IsComplete = true;
                }
                else
                {
                    QueueTouch(device, 0, X, Y, UnityEngine.InputSystem.TouchPhase.Stationary);
                }
            }
        }

        public class DragSequence : InputSequence
        {
            public float StartX, StartY, EndX, EndY;
            private bool began;

            public DragSequence(float sx, float sy, float ex, float ey, float duration = 0.3f, string action = "drag")
            {
                StartX = sx; StartY = sy; EndX = ex; EndY = ey;
                Duration = duration; Action = action;
            }

            public override void Update(Touchscreen device, float deltaTime)
            {
                Elapsed += deltaTime;
                float t = Mathf.Clamp01(Elapsed / Duration);
                float cx = Mathf.Lerp(StartX, EndX, t);
                float cy = Mathf.Lerp(StartY, EndY, t);

                if (!began)
                {
                    QueueTouch(device, 0, StartX, StartY, UnityEngine.InputSystem.TouchPhase.Began);
                    began = true;
                }
                else if (t >= 1f)
                {
                    QueueTouch(device, 0, EndX, EndY, UnityEngine.InputSystem.TouchPhase.Ended);
                    IsComplete = true;
                }
                else
                {
                    QueueTouch(device, 0, cx, cy, UnityEngine.InputSystem.TouchPhase.Moved);
                }
            }
        }

        public class PinchSequence : InputSequence
        {
            public float CenterX, CenterY, StartDistance, EndDistance;
            private bool began;

            public PinchSequence(float cx, float cy, float startDist, float endDist, float duration = 0.5f)
            {
                CenterX = cx; CenterY = cy;
                StartDistance = startDist; EndDistance = endDist;
                Duration = duration; Action = "pinch";
            }

            public override void Update(Touchscreen device, float deltaTime)
            {
                Elapsed += deltaTime;
                float t = Mathf.Clamp01(Elapsed / Duration);
                float dist = Mathf.Lerp(StartDistance, EndDistance, t);
                float halfDist = dist * 0.5f;

                // Two touches: left and right of center
                float x0 = CenterX - halfDist;
                float x1 = CenterX + halfDist;

                if (!began)
                {
                    QueueTouch(device, 0, x0, CenterY, UnityEngine.InputSystem.TouchPhase.Began);
                    QueueTouch(device, 1, x1, CenterY, UnityEngine.InputSystem.TouchPhase.Began);
                    began = true;
                }
                else if (t >= 1f)
                {
                    QueueTouch(device, 0, x0, CenterY, UnityEngine.InputSystem.TouchPhase.Ended);
                    QueueTouch(device, 1, x1, CenterY, UnityEngine.InputSystem.TouchPhase.Ended);
                    IsComplete = true;
                }
                else
                {
                    QueueTouch(device, 0, x0, CenterY, UnityEngine.InputSystem.TouchPhase.Moved);
                    QueueTouch(device, 1, x1, CenterY, UnityEngine.InputSystem.TouchPhase.Moved);
                }
            }
        }

        public class MultiTapSequence : InputSequence
        {
            public float X, Y;
            public int Count;
            public float Interval;
            public float TapDuration;

            private int currentTap;
            private float tapTimer;
            private bool inTap;

            public MultiTapSequence(float x, float y, int count = 2, float interval = 0.15f, float tapDuration = 0.05f)
            {
                X = x; Y = y; Count = count; Interval = interval; TapDuration = tapDuration;
                Duration = count * tapDuration + (count - 1) * interval;
                Action = "multi_tap";
            }

            public override void Update(Touchscreen device, float deltaTime)
            {
                Elapsed += deltaTime;
                tapTimer += deltaTime;

                if (currentTap >= Count)
                {
                    IsComplete = true;
                    return;
                }

                if (!inTap)
                {
                    // Start a tap
                    QueueTouch(device, 0, X, Y, UnityEngine.InputSystem.TouchPhase.Began);
                    inTap = true;
                    tapTimer = 0f;
                }
                else if (tapTimer >= TapDuration)
                {
                    // End the tap
                    QueueTouch(device, 0, X, Y, UnityEngine.InputSystem.TouchPhase.Ended);
                    inTap = false;
                    currentTap++;
                    tapTimer = -Interval; // wait for interval before next tap
                }
                else
                {
                    QueueTouch(device, 0, X, Y, UnityEngine.InputSystem.TouchPhase.Stationary);
                }
            }
        }

        // --- Core API ---

        public static void Initialize()
        {
            if (initialized) return;

            virtualTouchscreen = InputSystem.AddDevice<Touchscreen>("VirtualTouchscreen");
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            initialized = true;

            Debug.Log("[UnityBridge] InputEmulator initialized with virtual Touchscreen");
        }

        public static string EnqueueInput(InputSequence sequence)
        {
            if (!initialized)
            {
                throw new InvalidOperationException(
                    "InputEmulator not initialized. Call Initialize() from the main thread first.");
            }

            var id = $"input_{nextInputId++}";
            sequence.InputId = id;

            lock (pendingLock)
            {
                pendingSequences.Add(sequence);
            }

            return id;
        }

        public static InputStatusData GetStatus()
        {
            var data = new InputStatusData();

            lock (pendingLock)
            {
                data.activeCount = activeSequences.Count + pendingSequences.Count;
            }

            var activeInfos = new List<ActiveInputInfo>();
            foreach (var seq in activeSequences)
            {
                activeInfos.Add(new ActiveInputInfo
                {
                    inputId = seq.InputId,
                    action = seq.Action,
                    progress = seq.Progress
                });
            }
            data.active = activeInfos.ToArray();

            lock (pendingLock)
            {
                data.completedCount = completedInputs.Count;
                int start = Math.Max(0, completedInputs.Count - 10);
                var recent = new List<CompletedInput>();
                for (int i = start; i < completedInputs.Count; i++)
                    recent.Add(completedInputs[i]);
                data.recentCompleted = recent.ToArray();
            }

            return data;
        }

        // --- Internal ---

        private static void Update()
        {
            if (virtualTouchscreen == null) return;

            // Move pending to active
            lock (pendingLock)
            {
                if (pendingSequences.Count > 0)
                {
                    activeSequences.AddRange(pendingSequences);
                    pendingSequences.Clear();
                }
            }

            if (activeSequences.Count == 0) return;

            float dt = Time.deltaTime > 0 ? Time.deltaTime : (1f / 60f);

            for (int i = activeSequences.Count - 1; i >= 0; i--)
            {
                var seq = activeSequences[i];
                seq.Update(virtualTouchscreen, dt);

                if (seq.IsComplete)
                {
                    activeSequences.RemoveAt(i);
                    lock (pendingLock)
                    {
                        completedInputs.Add(new CompletedInput
                        {
                            inputId = seq.InputId,
                            action = seq.Action,
                            completedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        });

                        // Keep only last 100 completed
                        if (completedInputs.Count > 100)
                            completedInputs.RemoveAt(0);
                    }
                }
            }
        }

        private static void QueueTouch(Touchscreen device, int touchIndex, float x, float y, UnityEngine.InputSystem.TouchPhase phase)
        {
            var touchState = new TouchState
            {
                touchId = touchIndex + 1,
                position = new Vector2(x, y),
                phase = phase
            };

            InputSystem.QueueStateEvent(device, touchState);
        }

        private static void Cleanup()
        {
            if (virtualTouchscreen != null)
            {
                InputSystem.RemoveDevice(virtualTouchscreen);
                virtualTouchscreen = null;
            }

            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;

            activeSequences.Clear();
            lock (pendingLock)
            {
                pendingSequences.Clear();
            }

            initialized = false;
            Debug.Log("[UnityBridge] InputEmulator cleaned up");
        }
    }
}
