using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SwimmingSystem
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(PlayerWaterDetector))]
    public class SwimController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Camera whose forward/right directions steer swimming. Leave empty to use the Main Camera.")]
        [SerializeField] Transform cameraTransform;
        [SerializeField] PlayerWaterDetector waterDetector;

        [Header("Input Actions (enabled only while swimming)")]
        [SerializeField] InputActionProperty moveAction = NewAction("Swim Move", InputActionType.Value, "Vector2");
        [SerializeField] InputActionProperty ascendAction = NewAction("Swim Up", InputActionType.Button);
        [SerializeField] InputActionProperty descendAction = NewAction("Swim Down", InputActionType.Button);
        [SerializeField] InputActionProperty sprintAction = NewAction("Swim Fast", InputActionType.Button);

        [Header("Movement")]
        [Tooltip("Top swimming speed in m/s.")]
        [SerializeField, Min(0f)] float swimSpeed = 4f;
        [SerializeField, Min(1f)] float sprintMultiplier = 1.6f;
        [Tooltip("Speed added by the ascend/descend inputs, in m/s.")]
        [SerializeField, Min(0f)] float verticalSpeed = 3f;
        [Tooltip("How quickly you reach your target speed while pressing input. Lower = more inertia.")]
        [SerializeField, Min(0.01f)] float acceleration = 4f;
        [Tooltip("How quickly you coast to a stop with no input. Lower = longer glide.")]
        [SerializeField, Min(0.01f)] float waterDrag = 2f;
        [Tooltip("Extra drag on any speed above what you can swim, so diving in from a height doesn't send you to the bottom.")]
        [SerializeField, Min(0f)] float highSpeedDrag = 4f;
        [Tooltip("Speed you drift when idle under water: positive floats up, 0 is neutral, negative slowly sinks.")]
        [SerializeField] float waterBuoyancy = 0.5f;
        [Tooltip("Momentum carried in from the ground controller is clamped to this speed (protects against velocity spikes).")]
        [SerializeField, Min(0f)] float maxEntrySpeed = 15f;

        [Header("Surface")]
        [Tooltip("How far the eyes float above the water surface.")]
        [SerializeField] float eyeHeightAboveWater = 0.15f;
        [Tooltip("When the eyes are less than this far under the surface, the player is pulled up to float (unless diving).")]
        [SerializeField, Min(0f)] float surfaceSnapDepth = 0.5f;
        [Tooltip("How strongly the player is pulled to the floating height.")]
        [SerializeField, Min(0.1f)] float floatStrength = 3f;
        [Tooltip("At the surface, look at least this far below the horizon while swimming forward to dive.")]
        [SerializeField, Range(0f, 60f)] float diveAngle = 15f;
        [Tooltip("Gentle up-and-down bob while floating, in meters (0 turns it off).")]
        [SerializeField, Min(0f)] float bobAmplitude = 0.03f;
        [SerializeField, Min(0f)] float bobFrequency = 0.4f;

        [Header("Climbing Out")]
        [Tooltip("Hold ascend at the surface while facing a ledge to climb out of the water.")]
        [SerializeField] bool enableClimbOut = true;
        [Tooltip("Highest ledge, measured from the water surface, that can be climbed onto.")]
        [SerializeField, Min(0f)] float maxClimbHeight = 1f;
        [SerializeField, Min(0.1f)] float climbSpeed = 3.5f;
        [SerializeField] LayerMask climbableLayers = ~0;

        [Header("Look (only if your ground controller also rotates the camera)")]
        [Tooltip("Turn on if the camera freezes while swimming, i.e. your ground controller handles mouse look " +
                 "in the same script that gets turned off.")]
        [SerializeField] bool handleLook;
        [SerializeField] InputActionProperty lookAction = NewAction("Swim Look", InputActionType.Value, "Vector2");
        [Tooltip("Transform to pitch up/down. Use the camera root if a Cinemachine camera follows it. Leave empty to use the camera.")]
        [SerializeField] Transform pitchTransform;
        [Tooltip("Degrees per pixel of mouse movement.")]
        [SerializeField, Min(0f)] float mouseSensitivity = 0.1f;
        [Tooltip("Degrees per second at full stick tilt.")]
        [SerializeField, Min(0f)] float stickSensitivity = 150f;
        [SerializeField, Range(0f, 90f)] float pitchLimit = 89f;

        [Header("Live State (read-only, watch these while testing)")]
        [SerializeField] Vector3 currentVelocity;
        [SerializeField] bool atSurface;
        [SerializeField] bool climbing;

        const float MaxClimbDuration = 1.5f;

        CharacterController controller;
        List<InputAction> actionsEnabledBySwimming = new List<InputAction>();
        float pitch;
        Vector3 climbTarget;
        bool climbRising;
        float climbTimer;

        public Vector3 CurrentVelocity { get { return currentVelocity; } }
        public bool IsAtSurface { get { return atSurface; } }
        public bool IsClimbing { get { return climbing; } }
        public float EyeHeightAboveWater { get { return eyeHeightAboveWater; } }

        void Reset()
        {
            waterDetector = GetComponent<PlayerWaterDetector>();
            AddDefaultBindings();
        }

        void Awake()
        {
            controller = GetComponent<CharacterController>();

            if (waterDetector == null) waterDetector = GetComponent<PlayerWaterDetector>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
            if (pitchTransform == null) pitchTransform = cameraTransform;

            AddDefaultBindings();
        }

        void OnEnable()
        {
            if (controller == null)
            {
                controller = GetComponent<CharacterController>();
            }

            currentVelocity = Vector3.ClampMagnitude(controller.velocity, maxEntrySpeed);
            climbing = false;

            if (pitchTransform != null)
            {
                pitch = NormalizeAngle(pitchTransform.localEulerAngles.x);
            }

            EnableAction(moveAction);
            EnableAction(ascendAction);
            EnableAction(descendAction);
            EnableAction(sprintAction);
            if (handleLook)
            {
                EnableAction(lookAction);
            }
        }

        void OnDisable()
        {
            for (int i = 0; i < actionsEnabledBySwimming.Count; i++)
            {
                actionsEnabledBySwimming[i].Disable();
            }
            actionsEnabledBySwimming.Clear();
            climbing = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (!controller.enabled) return;

            if (handleLook)
            {
                UpdateLook(dt);
            }

            if (climbing)
            {
                UpdateClimb(dt);
                return;
            }

            Transform view = cameraTransform;
            if (view == null)
            {
                view = transform;
            }

            Vector2 moveInput = Vector2.ClampMagnitude(ReadVector2(moveAction), 1f);
            float verticalInput = Mathf.Clamp(ReadFloat(ascendAction) - ReadFloat(descendAction), -1f, 1f);
            bool sprinting = ReadFloat(sprintAction) > 0.5f;
            bool moving = moveInput.sqrMagnitude > 0.0001f;
            bool hasInput = moving || Mathf.Abs(verticalInput) > 0.01f;

            float speed = swimSpeed;
            if (sprinting)
            {
                speed = swimSpeed * sprintMultiplier;
            }

            Vector3 targetVelocity = (view.forward * moveInput.y + view.right * moveInput.x) * speed;
            targetVelocity.y += verticalInput * verticalSpeed;

            bool hasWater = waterDetector.ActiveVolume != null;
            float surfaceY = waterDetector.SurfaceHeight;
            float eyeY = waterDetector.EyePosition.y;
            atSurface = hasWater && eyeY > surfaceY - surfaceSnapDepth;

            float diveThreshold = -speed * Mathf.Sin(diveAngle * Mathf.Deg2Rad);
            bool diving = verticalInput < -0.1f || targetVelocity.y < diveThreshold;

            if (atSurface && !diving)
            {
                bool wantsToClimb = enableClimbOut && verticalInput > 0.5f;
                if (wantsToClimb && TryStartClimb(view))
                {
                    return;
                }

                targetVelocity = SwimAtSurface(view, moveInput, speed, surfaceY, eyeY, dt);
            }
            else
            {
                targetVelocity = SwimUnderwater(targetVelocity, hasWater, hasInput, dt);
            }

            SlowDownIfTooFast(speed, targetVelocity, dt);
            controller.Move(currentVelocity * dt);
        }

        Vector3 SwimAtSurface(Transform view, Vector2 moveInput, float speed, float surfaceY, float eyeY, float dt)
        {
            Vector3 flatForward = FlatForward(view);
            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);
            Vector3 targetVelocity = (flatForward * moveInput.y + flatRight * moveInput.x) * speed;

            float horizontalRate = waterDrag;
            if (moveInput.sqrMagnitude > 0.0001f)
            {
                horizontalRate = acceleration;
            }

            Vector3 currentHorizontal = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
            Vector3 newHorizontal = Vector3.Lerp(currentHorizontal, targetVelocity, SmoothingFactor(horizontalRate, dt));

            float bob = Mathf.Sin(Time.time * bobFrequency * 2f * Mathf.PI) * bobAmplitude;
            float floatHeight = surfaceY + eyeHeightAboveWater + bob;
            float springSpeed = (floatHeight - eyeY) * floatStrength;
            springSpeed = Mathf.Clamp(springSpeed, -verticalSpeed, verticalSpeed);
            float newVertical = Mathf.Lerp(currentVelocity.y, springSpeed, SmoothingFactor(2f * floatStrength, dt));

            currentVelocity = new Vector3(newHorizontal.x, newVertical, newHorizontal.z);

            targetVelocity.y = springSpeed;
            return targetVelocity;
        }

        Vector3 SwimUnderwater(Vector3 targetVelocity, bool hasWater, bool hasInput, float dt)
        {
            if (!hasWater)
            {
                targetVelocity.y -= verticalSpeed;
            }
            else if (!hasInput)
            {
                targetVelocity.y += waterBuoyancy;
            }

            float rate = waterDrag;
            if (hasInput)
            {
                rate = acceleration;
            }

            currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, SmoothingFactor(rate, dt));
            return targetVelocity;
        }

        void SlowDownIfTooFast(float speed, Vector3 targetVelocity, float dt)
        {
            float speedLimit = Mathf.Max(speed, targetVelocity.magnitude);
            float currentSpeed = currentVelocity.magnitude;
            if (currentSpeed <= speedLimit) return;

            float extraSpeed = currentSpeed - speedLimit;
            extraSpeed = extraSpeed * Mathf.Exp(-highSpeedDrag * dt);
            currentVelocity = currentVelocity.normalized * (speedLimit + extraSpeed);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!enabled) return;
            if (climbing) return;

            float speedIntoSurface = Vector3.Dot(currentVelocity, hit.normal);
            if (speedIntoSurface < 0f)
            {
                currentVelocity = currentVelocity - hit.normal * speedIntoSurface;
            }
        }

        bool TryStartClimb(Transform view)
        {
            float surfaceY = waterDetector.SurfaceHeight;
            float radius = GetScaledRadius();
            float height = GetScaledHeight();
            Vector3 feet = GetFeetPosition();
            Vector3 forward = FlatForward(view);

            Vector3 wallCheckStart = new Vector3(feet.x, surfaceY - 0.1f, feet.z);
            RaycastHit wallHit;
            bool hitWall = Physics.Raycast(wallCheckStart, forward, out wallHit, radius + 0.4f,
                                           climbableLayers, QueryTriggerInteraction.Ignore);
            if (!hitWall) return false;

            float wallAngle = Vector3.Angle(wallHit.normal, Vector3.up);
            if (wallAngle <= controller.slopeLimit) return false;

            Vector3 ledgeCheckStart = wallHit.point + forward * (radius + 0.1f);
            ledgeCheckStart.y = surfaceY + maxClimbHeight + 0.05f;
            RaycastHit ledgeHit;
            bool hitLedge = Physics.Raycast(ledgeCheckStart, Vector3.down, out ledgeHit, maxClimbHeight + 0.5f,
                                            climbableLayers, QueryTriggerInteraction.Ignore);
            if (!hitLedge) return false;

            float ledgeAngle = Vector3.Angle(ledgeHit.normal, Vector3.up);
            if (ledgeAngle > controller.slopeLimit) return false;

            Vector3 standPosition = ledgeHit.point + Vector3.up * (controller.skinWidth + 0.02f);
            Vector3 lowSphereCenter = standPosition + Vector3.up * radius;
            Vector3 highSphereCenter = standPosition + Vector3.up * (height - radius);
            bool blocked = Physics.CheckCapsule(lowSphereCenter, highSphereCenter, radius * 0.95f,
                                                climbableLayers, QueryTriggerInteraction.Ignore);
            if (blocked) return false;

            climbing = true;
            climbRising = true;
            climbTimer = 0f;
            climbTarget = standPosition;
            currentVelocity = Vector3.zero;
            return true;
        }

        void UpdateClimb(float dt)
        {
            climbTimer += dt;
            Vector3 feet = GetFeetPosition();
            Vector3 movement;

            if (climbRising)
            {
                float heightLeft = climbTarget.y - feet.y;
                if (heightLeft <= 0.01f)
                {
                    climbRising = false;
                }

                float riseAmount = Mathf.Clamp(heightLeft, 0f, climbSpeed * dt);
                movement = Vector3.up * riseAmount;
            }
            else
            {
                Vector3 toTarget = climbTarget - feet;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;

                if (distance <= 0.02f)
                {
                    FinishClimb();
                    return;
                }

                float stepLength = Mathf.Min(distance, climbSpeed * dt);
                movement = toTarget.normalized * stepLength;
            }

            controller.Move(movement);

            if (climbTimer > MaxClimbDuration)
            {
                climbing = false;
            }
        }

        void FinishClimb()
        {
            controller.Move(Vector3.down * 0.1f);
            currentVelocity = Vector3.zero;
            climbing = false;
        }

        float GetScaledRadius()
        {
            Vector3 scale = transform.lossyScale;
            float widestScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            return controller.radius * widestScale;
        }

        float GetScaledHeight()
        {
            float height = controller.height * Mathf.Abs(transform.lossyScale.y);
            return Mathf.Max(height, GetScaledRadius() * 2f);
        }

        Vector3 GetFeetPosition()
        {
            Vector3 center = transform.TransformPoint(controller.center);
            return center + Vector3.down * (GetScaledHeight() * 0.5f);
        }

        void UpdateLook(float dt)
        {
            InputAction action = lookAction.action;
            if (action == null) return;
            if (!action.enabled) return;
            if (pitchTransform == null) return;

            Vector2 look = action.ReadValue<Vector2>();
            if (look.sqrMagnitude < 0.000001f) return;

            bool usingMouse = action.activeControl != null && action.activeControl.device is Pointer;

            Vector2 delta;
            if (usingMouse)
            {
                delta = look * mouseSensitivity;
            }
            else
            {
                delta = look * (stickSensitivity * dt);
            }

            transform.Rotate(0f, delta.x, 0f);
            pitch = Mathf.Clamp(pitch - delta.y, -pitchLimit, pitchLimit);
            pitchTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        static float SmoothingFactor(float rate, float dt)
        {
            return 1f - Mathf.Exp(-rate * dt);
        }

        static Vector3 FlatForward(Transform view)
        {
            Vector3 forward = view.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                if (view.forward.y > 0f)
                {
                    forward = -view.up;
                }
                else
                {
                    forward = view.up;
                }
                forward.y = 0f;
            }

            return forward.normalized;
        }

        static float NormalizeAngle(float degrees)
        {
            if (degrees > 180f)
            {
                return degrees - 360f;
            }
            else
            {
                return degrees;
            }
        }

        static Vector2 ReadVector2(InputActionProperty property)
        {
            InputAction action = property.action;
            if (action != null && action.enabled)
            {
                return action.ReadValue<Vector2>();
            }
            else
            {
                return Vector2.zero;
            }
        }

        static float ReadFloat(InputActionProperty property)
        {
            InputAction action = property.action;
            if (action != null && action.enabled)
            {
                return action.ReadValue<float>();
            }
            else
            {
                return 0f;
            }
        }

        void EnableAction(InputActionProperty property)
        {
            InputAction action = property.action;
            if (action == null) return;
            if (action.enabled) return;

            action.Enable();
            actionsEnabledBySwimming.Add(action);
        }

        static InputActionProperty NewAction(string name, InputActionType type, string controlType = null)
        {
            InputAction action = new InputAction(name, type, expectedControlType: controlType);
            return new InputActionProperty(action);
        }

        static bool NeedsDefaultBindings(InputActionProperty property)
        {
            if (property.reference != null) return false;
            if (property.action == null) return false;
            return property.action.bindings.Count == 0;
        }

        void AddDefaultBindings()
        {
            if (NeedsDefaultBindings(moveAction))
            {
                InputAction move = moveAction.action;
                move.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");
                move.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/upArrow")
                    .With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/leftArrow")
                    .With("Right", "<Keyboard>/rightArrow");
                move.AddBinding("<Gamepad>/leftStick");
            }

            if (NeedsDefaultBindings(ascendAction))
            {
                ascendAction.action.AddBinding("<Keyboard>/space");
                ascendAction.action.AddBinding("<Gamepad>/buttonSouth");
            }

            if (NeedsDefaultBindings(descendAction))
            {
                descendAction.action.AddBinding("<Keyboard>/leftCtrl");
                descendAction.action.AddBinding("<Keyboard>/c");
                descendAction.action.AddBinding("<Gamepad>/buttonEast");
            }

            if (NeedsDefaultBindings(sprintAction))
            {
                sprintAction.action.AddBinding("<Keyboard>/leftShift");
                sprintAction.action.AddBinding("<Gamepad>/leftStickPress");
            }

            if (NeedsDefaultBindings(lookAction))
            {
                lookAction.action.AddBinding("<Mouse>/delta");
                lookAction.action.AddBinding("<Gamepad>/rightStick");
            }
        }
    }
}
