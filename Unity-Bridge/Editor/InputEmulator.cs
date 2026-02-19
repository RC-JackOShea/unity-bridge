using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace UnityBridge
{
    /// <summary>
    /// Input simulation engine.
    /// For tap/hold/click: uses ExecuteEvents to directly invoke UI pointer events
    /// (reliable in the editor where Input System event routing is unreliable).
    /// For gestures (drag/swipe/pinch): uses virtual Touchscreen device.
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

            public abstract void Update(float deltaTime);
        }

        // --- Tap/Hold via ExecuteEvents (reliable for UI) ---

        public class TapSequence : InputSequence
        {
            public float X, Y;

            public TapSequence(float x, float y, float duration = 0.05f)
            {
                X = x; Y = y; Duration = duration; Action = "tap";
            }

            public override void Update(float deltaTime)
            {
                // Execute immediately on first update
                SimulatePointerClick(X, Y);
                IsComplete = true;
            }
        }

        public class HoldSequence : InputSequence
        {
            public float X, Y;
            private bool pressed;
            private GameObject target;
            private PointerEventData pointerData;

            public HoldSequence(float x, float y, float duration = 1.0f)
            {
                X = x; Y = y; Duration = duration; Action = "hold";
            }

            public override void Update(float deltaTime)
            {
                Elapsed += deltaTime;

                if (!pressed)
                {
                    var result = RaycastUI(X, Y, out pointerData);
                    if (result != null)
                    {
                        target = result;
                        pointerData.pointerPressRaycast = pointerData.pointerCurrentRaycast;
                        ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerDownHandler);
                    }
                    pressed = true;
                }
                else if (Elapsed >= Duration)
                {
                    if (target != null && pointerData != null)
                    {
                        ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerUpHandler);
                        ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerClickHandler);
                    }
                    IsComplete = true;
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

            public MultiTapSequence(float x, float y, int count = 2, float interval = 0.15f, float tapDuration = 0.05f)
            {
                X = x; Y = y; Count = count; Interval = interval; TapDuration = tapDuration;
                Duration = count * tapDuration + (count - 1) * interval;
                Action = "multi_tap";
            }

            public override void Update(float deltaTime)
            {
                Elapsed += deltaTime;
                tapTimer += deltaTime;

                if (currentTap >= Count)
                {
                    IsComplete = true;
                    return;
                }

                if (tapTimer >= Interval || currentTap == 0)
                {
                    SimulatePointerClick(X, Y);
                    currentTap++;
                    tapTimer = 0f;
                }
            }
        }

        // --- Touch-based sequences (for gestures) ---

        public class DragSequence : InputSequence
        {
            public float StartX, StartY, EndX, EndY;
            private bool began;

            public DragSequence(float sx, float sy, float ex, float ey, float duration = 0.3f, string action = "drag")
            {
                StartX = sx; StartY = sy; EndX = ex; EndY = ey;
                Duration = duration; Action = action;
            }

            public override void Update(float deltaTime)
            {
                Elapsed += deltaTime;
                float t = Mathf.Clamp01(Elapsed / Duration);
                float cx = Mathf.Lerp(StartX, EndX, t);
                float cy = Mathf.Lerp(StartY, EndY, t);

                if (!began)
                {
                    QueueTouch(0, StartX, StartY, UnityEngine.InputSystem.TouchPhase.Began);
                    began = true;
                }
                else if (t >= 1f)
                {
                    QueueTouch(0, EndX, EndY, UnityEngine.InputSystem.TouchPhase.Ended);
                    IsComplete = true;
                }
                else
                {
                    QueueTouch(0, cx, cy, UnityEngine.InputSystem.TouchPhase.Moved);
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

            public override void Update(float deltaTime)
            {
                Elapsed += deltaTime;
                float t = Mathf.Clamp01(Elapsed / Duration);
                float dist = Mathf.Lerp(StartDistance, EndDistance, t);
                float halfDist = dist * 0.5f;

                float x0 = CenterX - halfDist;
                float x1 = CenterX + halfDist;

                if (!began)
                {
                    QueueTouch(0, x0, CenterY, UnityEngine.InputSystem.TouchPhase.Began);
                    QueueTouch(1, x1, CenterY, UnityEngine.InputSystem.TouchPhase.Began);
                    began = true;
                }
                else if (t >= 1f)
                {
                    QueueTouch(0, x0, CenterY, UnityEngine.InputSystem.TouchPhase.Ended);
                    QueueTouch(1, x1, CenterY, UnityEngine.InputSystem.TouchPhase.Ended);
                    IsComplete = true;
                }
                else
                {
                    QueueTouch(0, x0, CenterY, UnityEngine.InputSystem.TouchPhase.Moved);
                    QueueTouch(1, x1, CenterY, UnityEngine.InputSystem.TouchPhase.Moved);
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

        // --- UI Pointer Simulation ---

        /// <summary>
        /// Raycast into the UI at screen coordinates (x, y).
        /// Returns the hit GameObject or null.
        /// </summary>
        private static GameObject RaycastUI(float x, float y, out PointerEventData pointerData)
        {
            pointerData = null;
            var es = EventSystem.current;
            if (es == null) return null;

            pointerData = new PointerEventData(es)
            {
                position = new Vector2(x, y),
                button = PointerEventData.InputButton.Left
            };

            var results = new List<RaycastResult>();
            es.RaycastAll(pointerData, results);

            if (results.Count > 0)
            {
                pointerData.pointerCurrentRaycast = results[0];
                return results[0].gameObject;
            }

            return null;
        }

        /// <summary>
        /// Simulate a full pointer click (down + up + click) at screen coordinates.
        /// Uses ExecuteEvents with hierarchy traversal so events bubble up to parent
        /// handlers (e.g., clicking a Text label inside a Button triggers the Button).
        /// </summary>
        private static void SimulatePointerClick(float x, float y)
        {
            var hitObject = RaycastUI(x, y, out var pointerData);

            if (hitObject == null)
            {
                Debug.Log($"[InputEmulator] Tap at ({x},{y}): no UI element found (non-UI tap)");
                QueueMouseEvent(x, y);
                return;
            }

            // Find the actual click handler by walking up the hierarchy
            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitObject);
            var downHandler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(hitObject);
            var upHandler = ExecuteEvents.GetEventHandler<IPointerUpHandler>(hitObject);

            var target = clickHandler ?? downHandler ?? hitObject;

            Debug.Log($"[InputEmulator] Tap at ({x},{y}): raycast hit '{hitObject.name}', click handler='{(clickHandler != null ? clickHandler.name : "none")}'");

            pointerData.pointerPressRaycast = pointerData.pointerCurrentRaycast;
            pointerData.pointerPress = target;
            pointerData.rawPointerPress = target;
            pointerData.eligibleForClick = true;

            // Full pointer event sequence
            if (downHandler != null)
                ExecuteEvents.Execute(downHandler, pointerData, ExecuteEvents.pointerDownHandler);
            if (upHandler != null)
                ExecuteEvents.Execute(upHandler, pointerData, ExecuteEvents.pointerUpHandler);
            if (clickHandler != null)
                ExecuteEvents.Execute(clickHandler, pointerData, ExecuteEvents.pointerClickHandler);
        }

        /// <summary>
        /// Fallback: queue a mouse press/release for non-UI game objects.
        /// </summary>
        private static void QueueMouseEvent(float x, float y)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            mouse.CopyState<MouseState>(out var pressState);
            pressState.position = new Vector2(x, y);
            pressState.buttons |= (ushort)1;
            InputSystem.QueueStateEvent(mouse, pressState);

            mouse.CopyState<MouseState>(out var releaseState);
            releaseState.position = new Vector2(x, y);
            releaseState.buttons &= unchecked((ushort)~1);
            InputSystem.QueueStateEvent(mouse, releaseState);
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
                seq.Update(dt);

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

                        if (completedInputs.Count > 100)
                            completedInputs.RemoveAt(0);
                    }
                }
            }
        }

        // --- Touch input helpers ---

        private static void QueueTouch(int touchIndex, float x, float y, UnityEngine.InputSystem.TouchPhase phase)
        {
            if (virtualTouchscreen == null) return;

            var touchState = new TouchState
            {
                touchId = touchIndex + 1,
                position = new Vector2(x, y),
                phase = phase
            };

            InputSystem.QueueDeltaStateEvent(virtualTouchscreen.touches[touchIndex], touchState);
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
