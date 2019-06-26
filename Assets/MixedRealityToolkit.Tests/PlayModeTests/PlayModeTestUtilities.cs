﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#if !WINDOWS_UWP
// When the .NET scripting backend is enabled and C# projects are built
// Unity doesn't include the the required assemblies (i.e. the ones below).
// Given that the .NET backend is deprecated by Unity at this point it's we have
// to work around this on our end.

using Microsoft.MixedReality.Toolkit.Utilities;
using Microsoft.MixedReality.Toolkit.Input;
using UnityEngine;
using NUnit.Framework;
using System.Collections;
using System.IO;
using Microsoft.MixedReality.Toolkit.Diagnostics;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

#if UNITY_EDITOR
using TMPro;
using UnityEditor;
#endif

namespace Microsoft.MixedReality.Toolkit.Tests
{
    public class PlayModeTestUtilities
    {
        // Unity's default scene name for a recently created scene
        const string playModeTestSceneName = "MixedRealityToolkit.PlayModeTestScene";

        /// <summary>
        /// Creates a play mode test scene, creates an MRTK instance, initializes playspace.
        /// </summary>
        public static void Setup()
        {
            if (EditorApplication.isPlaying)
            {
                bool sceneExists = false;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene playModeTestScene = SceneManager.GetSceneAt(i);
                    if (playModeTestScene.name == playModeTestSceneName && playModeTestScene.isLoaded)
                    {
                        SceneManager.SetActiveScene(playModeTestScene);
                        sceneExists = true;
                    }
                }

                if (!sceneExists)
                {
                    Scene playModeTestScene = SceneManager.CreateScene(playModeTestSceneName);
                    SceneManager.SetActiveScene(playModeTestScene);
                }
            }
            else
            {
                // At the very start of testing, playmode may not be active.
                // So we'll use the editor scene manager instead of the scene manaager.
                bool sceneExists = false;
                for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                {
                    Scene playModeTestScene = EditorSceneManager.GetSceneAt(i);
                    if (playModeTestScene.name == playModeTestSceneName && playModeTestScene.isLoaded)
                    {
                        EditorSceneManager.SetActiveScene(playModeTestScene);
                        sceneExists = true;
                    }
                }

                if (!sceneExists)
                {   // In this case, create a fresh scene. This scene's name will be empty. We'll check for it on teardown.
                    Scene playModeTestScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
                    EditorSceneManager.SetActiveScene(playModeTestScene);
                }
            }

            // Create an MRTK instance and set up playspace
            TestUtilities.InitializeMixedRealityToolkit(true);
            TestUtilities.InitializePlayspace(); 
        }

        /// <summary>
        /// Destroys all objects in the play mode test scene, if it has been loaded, and shuts down MRTK instance.
        /// </summary>
        /// <returns></returns>
        public static void TearDown()
        {
            TestUtilities.ShutdownMixedRealityToolkit();

            Scene playModeTestScene = SceneManager.GetSceneByName(playModeTestSceneName);
            if (playModeTestScene.isLoaded)
            {
                foreach (GameObject gameObject in playModeTestScene.GetRootGameObjects())
                {
                    GameObject.Destroy(gameObject);
                }
            }

            // If we created a temporary untitled scene in edit mode to get us started, unload that now
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene editorScene = SceneManager.GetSceneAt(i);
                if (string.IsNullOrEmpty(editorScene.name))
                {   // We've found our editor scene. Unload it.
                    SceneManager.UnloadSceneAsync(editorScene);
                }
            }
        }

        public static SimulatedHandData.HandJointDataGenerator GenerateHandPose(ArticulatedHandPose.GestureId gesture, Handedness handedness, Vector3 worldPosition)
        {
            return (jointsOut) =>
            {
                ArticulatedHandPose gesturePose = ArticulatedHandPose.GetGesturePose(gesture);
                Quaternion rotation = Quaternion.identity;
                gesturePose.ComputeJointPoses(handedness, rotation, worldPosition, jointsOut);
            };
        }

        public static IMixedRealityInputSystem GetInputSystem()
        {
            IMixedRealityInputSystem inputSystem;
            MixedRealityServiceRegistry.TryGetService(out inputSystem);
            Assert.IsNotNull(inputSystem, "MixedRealityInputSystem is null!");
            return inputSystem;
        }

        public static InputSimulationService GetInputSimulationService()
        {
            IMixedRealityInputSystem inputSystem = GetInputSystem();
            InputSimulationService inputSimulationService = (inputSystem as IMixedRealityDataProviderAccess).GetDataProvider<InputSimulationService>();
            Assert.IsNotNull(inputSimulationService, "InputSimulationService is null!");
            inputSimulationService.UserInputEnabled = false;
            return inputSimulationService;
        }

        /// <summary>
        /// Initializes the MRTK such that there are no other input system listeners
        /// (global or per-interface).
        /// </summary>
        internal static IEnumerator SetupMrtkWithoutGlobalInputHandlers()
        {
            TestUtilities.InitializeMixedRealityToolkitAndCreateScenes(true);
            TestUtilities.InitializePlayspace();

            IMixedRealityInputSystem inputSystem = null;
            MixedRealityServiceRegistry.TryGetService(out inputSystem);

            Assert.IsNotNull(inputSystem, "Input system must be initialized");

            // Let input system to register all cursors and managers.
            yield return null;

            // Switch off / Destroy all input components, which listen to global events
            Object.Destroy(inputSystem.GazeProvider.GazeCursor as Behaviour);
            inputSystem.GazeProvider.Enabled = false;

            var diagnosticsVoiceControls = Object.FindObjectsOfType<DiagnosticsSystemVoiceControls>();
            foreach (var diagnosticsComponent in diagnosticsVoiceControls)
            {
                diagnosticsComponent.enabled = false;
            }

            // Let objects be destroyed
            yield return null;

            // Forcibly unregister all other input event listeners.
            BaseEventSystem baseEventSystem = inputSystem as BaseEventSystem;
            MethodInfo unregisterHandler = baseEventSystem.GetType().GetMethod("UnregisterHandler");

            // Since we are iterating over and removing these values, we need to snapshot them
            // before calling UnregisterHandler on each handler.
            var eventHandlersByType = new Dictionary<System.Type, List<BaseEventSystem.EventHandlerEntry>>(((BaseEventSystem)inputSystem).EventHandlersByType);
            foreach (var typeToEventHandlers in eventHandlersByType)
            {
                var handlerEntries = new List<BaseEventSystem.EventHandlerEntry>(typeToEventHandlers.Value);
                foreach (var handlerEntry in handlerEntries)
                {
                    unregisterHandler.MakeGenericMethod(typeToEventHandlers.Key)
                        .Invoke(baseEventSystem, 
                                new object[] { handlerEntry.handler });
                }
            }

            // Check that input system is clean
            CollectionAssert.IsEmpty(((BaseEventSystem)inputSystem).EventListeners,      "Input event system handler registry is not empty in the beginning of the test.");
            CollectionAssert.IsEmpty(((BaseEventSystem)inputSystem).EventHandlersByType, "Input event system handler registry is not empty in the beginning of the test.");

            yield return null;
        }

        internal static IEnumerator MoveHandFromTo(Vector3 startPos, Vector3 endPos, int numSteps, ArticulatedHandPose.GestureId gestureId, Handedness handedness, InputSimulationService inputSimulationService)
        {
            Debug.Assert(handedness == Handedness.Right || handedness == Handedness.Left, "handedness must be either right or left");
            bool isPinching = gestureId == ArticulatedHandPose.GestureId.Grab || gestureId == ArticulatedHandPose.GestureId.Pinch || gestureId == ArticulatedHandPose.GestureId.PinchSteadyWrist;
            for (int i = 0; i < numSteps; i++)
            {
                float t = numSteps > 1 ? 1.0f / (numSteps - 1) * i : 1.0f;
                Vector3 handPos = Vector3.Lerp(startPos, endPos, t);
                var handDataGenerator = GenerateHandPose(
                        gestureId,
                        handedness,
                        handPos);
                SimulatedHandData toUpdate = handedness == Handedness.Right ? inputSimulationService.HandDataRight : inputSimulationService.HandDataLeft;
                inputSimulationService.HandDataRight.Update(true, isPinching, handDataGenerator);
                yield return null;
            }
        }

        internal static IEnumerator HideHand(Handedness handedness, InputSimulationService inputSimulationService)
        {
            SimulatedHandData toUpdate = handedness == Handedness.Right ? inputSimulationService.HandDataRight : inputSimulationService.HandDataLeft;
            inputSimulationService.HandDataRight.Update(false, false, GenerateHandPose(ArticulatedHandPose.GestureId.Open, handedness, Vector3.zero));
            // Wait one frame for the hand to actually appear
            yield return null;
        }

        internal static IEnumerator ShowHand(Handedness handedness, InputSimulationService inputSimulationService)
        {
            SimulatedHandData toUpdate = handedness == Handedness.Right ? inputSimulationService.HandDataRight : inputSimulationService.HandDataLeft;
            inputSimulationService.HandDataRight.Update(true, false, GenerateHandPose(ArticulatedHandPose.GestureId.Open, handedness, Vector3.zero));
            // Wait one frame for the hand to actually go away
            yield return null;
        }

        internal static void EnsureTextMeshProEssentials()
        {
#if UNITY_EDITOR
            // Special handling for TMP Settings and importing Essential Resources
            if (TMP_Settings.instance == null)
            {
                string packageFullPath = Path.GetFullPath("Packages/com.unity.textmeshpro");
                if (Directory.Exists(packageFullPath))
                {
                    AssetDatabase.ImportPackage(packageFullPath + "/Package Resources/TMP Essential Resources.unitypackage", false);
                }
            }
#endif
        }
    }
}
#endif