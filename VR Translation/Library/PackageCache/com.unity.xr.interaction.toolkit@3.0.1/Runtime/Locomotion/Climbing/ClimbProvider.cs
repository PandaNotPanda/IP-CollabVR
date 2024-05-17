﻿using System;
using System.Collections.Generic;
using UnityEngine.Assertions;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing
{
    /// <summary>
    /// Locomotion provider that allows the user to climb a <see cref="ClimbInteractable"/> by selecting it.
    /// Climb locomotion moves the XR Origin counter to movement of the last selecting interactor, with optional
    /// movement constraints along each axis of the interactable.
    /// </summary>
    /// <seealso cref="ClimbInteractable"/>
    [AddComponentMenu("XR/Locomotion/Climb Provider", 11)]
    [HelpURL(XRHelpURLConstants.k_ClimbProvider)]
    [MovedFrom("UnityEngine.XR.Interaction.Toolkit")]
    public class ClimbProvider : LocomotionProvider
    {
        // These are parallel lists, where each interactor and its grabbed interactable share the same index in each list.
        // The last item in each list represents the most recent selection, which is the only one that actually drives movement.
        readonly List<IXRSelectInteractor> m_GrabbingInteractors = new List<IXRSelectInteractor>();
        readonly List<ClimbInteractable> m_GrabbedClimbables = new List<ClimbInteractable>();

        Vector3 m_InteractorAnchorWorldPosition;
        Vector3 m_InteractorAnchorClimbSpacePosition;

        [SerializeField]
        [Tooltip("Climb locomotion settings. Can be overridden by the Climb Interactable used for locomotion.")]
        ClimbSettingsDatumProperty m_ClimbSettings = new ClimbSettingsDatumProperty(new ClimbSettings());

        /// <summary>
        /// Climb locomotion settings. Can be overridden by the <see cref="ClimbInteractable"/> used for locomotion.
        /// </summary>
        public ClimbSettingsDatumProperty climbSettings
        {
            get => m_ClimbSettings;
            set => m_ClimbSettings = value;
        }

        /// <summary>
        /// The interactable that is currently grabbed and driving movement. This will be <see langword="null"/> if
        /// there is no active climb.
        /// </summary>
        public ClimbInteractable climbAnchorInteractable
        {
            get
            {
                if (m_GrabbedClimbables.Count > 0)
                    return m_GrabbedClimbables[m_GrabbedClimbables.Count - 1];
                return null;
            }
        }

        /// <summary>
        /// The interactor that is currently grabbing and driving movement. This will be <see langword="null"/> if
        /// there is no active climb.
        /// </summary>
        public IXRSelectInteractor climbAnchorInteractor
        {
            get
            {
                if (m_GrabbingInteractors.Count > 0)
                    return m_GrabbingInteractors[m_GrabbingInteractors.Count - 1];
                return null;
            }
        }

        /// <summary>
        /// The transformation that is used by this component to apply climb movement.
        /// </summary>
        public XROriginMovement transformation { get; set; } = new XROriginMovement { forceUnconstrained = true };

        /// <summary>
        /// Calls the methods in its invocation list when the provider updates <see cref="climbAnchorInteractable"/>
        /// and <see cref="climbAnchorInteractor"/>. This can be invoked from either <see cref="StartClimbGrab"/> or
        /// <see cref="FinishClimbGrab"/>. This is not invoked when climb locomotion ends.
        /// </summary>
        public event Action<ClimbProvider> climbAnchorUpdated;

        /// <inheritdoc />
        protected override void Awake()
        {
            base.Awake();
            if (m_ClimbSettings == null || m_ClimbSettings.Value == null)
                m_ClimbSettings = new ClimbSettingsDatumProperty(new ClimbSettings());
        }

        /// <summary>
        /// Starts a grab as part of climbing <paramref name="climbInteractable"/>, using the position of
        /// <paramref name="interactor"/> to drive movement.
        /// </summary>
        /// <param name="climbInteractable">The object to climb.</param>
        /// <param name="interactor">The interactor that initiates the grab and drives movement.</param>
        /// <remarks>
        /// This puts the <see cref="LocomotionProvider.locomotionPhase"/> in the <see cref="LocomotionPhase.Started"/>
        /// state if locomotion has not already started. The phase will then enter the <see cref="LocomotionPhase.Moving"/>
        /// state in the next <see cref="Update"/>.
        /// </remarks>
        public void StartClimbGrab(ClimbInteractable climbInteractable, IXRSelectInteractor interactor)
        {
            var xrOrigin = mediator.xrOrigin?.Origin;
            if (xrOrigin == null)
                return;

            m_GrabbingInteractors.Add(interactor);
            m_GrabbedClimbables.Add(climbInteractable);
            UpdateClimbAnchor(climbInteractable, interactor);

            TryPrepareLocomotion();
        }

        /// <summary>
        /// Finishes the grab driven by <paramref name="interactor"/>. If this was the most recent grab then movement
        /// will now be driven by the next most recent grab.
        /// </summary>
        /// <param name="interactor">The interactor whose grab to finish.</param>
        /// <remarks>
        /// If there is no other active grab to fall back on, this will put the <see cref="LocomotionProvider.locomotionPhase"/>
        /// in the <see cref="LocomotionPhase.Done"/> state in the next <see cref="Update"/>.
        /// </remarks>
        public void FinishClimbGrab(IXRSelectInteractor interactor)
        {
            var interactionIndex = m_GrabbingInteractors.IndexOf(interactor);
            if (interactionIndex < 0)
                return;

            Assert.AreEqual(m_GrabbingInteractors.Count, m_GrabbedClimbables.Count);

            if (interactionIndex > 0 && interactionIndex == m_GrabbingInteractors.Count - 1)
            {
                // If this was the most recent grab then the interactor driving movement will change,
                // so we need to update the anchor position.
                var newLastIndex = interactionIndex - 1;
                UpdateClimbAnchor(m_GrabbedClimbables[newLastIndex], m_GrabbingInteractors[newLastIndex]);
            }

            m_GrabbingInteractors.RemoveAt(interactionIndex);
            m_GrabbedClimbables.RemoveAt(interactionIndex);
        }

        void UpdateClimbAnchor(ClimbInteractable climbInteractable, IXRInteractor interactor)
        {
            var climbTransform = climbInteractable.climbTransform;
            m_InteractorAnchorWorldPosition = interactor.transform.position;
            m_InteractorAnchorClimbSpacePosition = climbTransform.InverseTransformPoint(m_InteractorAnchorWorldPosition);
            climbAnchorUpdated?.Invoke(this);
        }

        /// <summary>
        /// See <see cref="MonoBehaviour"/>.
        /// </summary>
        protected virtual void Update()
        {
            if (!isLocomotionActive)
                return;

            // Use the most recent interaction to drive movement
            if (m_GrabbingInteractors.Count > 0)
            {
                if (locomotionState == LocomotionState.Preparing)
                    TryStartLocomotionImmediately();

                Assert.AreEqual(m_GrabbingInteractors.Count, m_GrabbedClimbables.Count);

                var lastIndex = m_GrabbingInteractors.Count - 1;
                var currentInteractor = m_GrabbingInteractors[lastIndex];
                var currentClimbInteractable = m_GrabbedClimbables[lastIndex];
                if (currentInteractor == null || currentClimbInteractable == null)
                {
                    FinishLocomotion();
                    return;
                }

                StepClimbMovement(currentClimbInteractable, currentInteractor);
            }
            else
            {
                FinishLocomotion();
            }
        }

        void StepClimbMovement(ClimbInteractable currentClimbInteractable, IXRSelectInteractor currentInteractor)
        {
            // Move rig such that climb interactor position stays constant
            var activeClimbSettings = GetActiveClimbSettings(currentClimbInteractable);
            var allowFreeXMovement = activeClimbSettings.allowFreeXMovement;
            var allowFreeYMovement = activeClimbSettings.allowFreeYMovement;
            var allowFreeZMovement = activeClimbSettings.allowFreeZMovement;
            var interactorWorldPosition = currentInteractor.transform.position;
            Vector3 movement;

            if (allowFreeXMovement && allowFreeYMovement && allowFreeZMovement)
            {
                // No need to check position relative to climbable object if movement is unconstrained
                movement = m_InteractorAnchorWorldPosition - interactorWorldPosition;
            }
            else
            {
                var climbTransform = currentClimbInteractable.climbTransform;
                var interactorClimbSpacePosition = climbTransform.InverseTransformPoint(interactorWorldPosition);
                var movementInClimbSpace = m_InteractorAnchorClimbSpacePosition - interactorClimbSpacePosition;

                if (!allowFreeXMovement)
                    movementInClimbSpace.x = 0f;

                if (!allowFreeYMovement)
                    movementInClimbSpace.y = 0f;

                if (!allowFreeZMovement)
                    movementInClimbSpace.z = 0f;

                movement = climbTransform.TransformVector(movementInClimbSpace);
            }

            transformation.motion = movement;
            TryQueueTransformation(transformation);
        }

        void FinishLocomotion()
        {
            TryEndLocomotion();
            m_GrabbingInteractors.Clear();
            m_GrabbedClimbables.Clear();
        }

        ClimbSettings GetActiveClimbSettings(ClimbInteractable climbInteractable)
        {
            if (climbInteractable.climbSettingsOverride.Value != null)
                return climbInteractable.climbSettingsOverride;

            return m_ClimbSettings;
        }
    }
}