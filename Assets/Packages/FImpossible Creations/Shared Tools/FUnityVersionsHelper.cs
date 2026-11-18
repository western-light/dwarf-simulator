#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace FIMSpace
{
    /// <summary> 
    /// FUVH - Fimpossible Unity Versions Helper. 
    /// It is implementing unity's #ifdefs so you can use methods from this class to avoid dirty code.
    /// </summary>
    public static class FUVH
    {

        /// <summary> Calling FindObjectOfType or FindAnyObjectByType depending on unity version </summary>
        public static T FindSceneObject<T>( bool includeInactive = false ) where T : UnityEngine.Object
        {
#if UNITY_6000_4_OR_NEWER
            return UnityEngine.Object.FindAnyObjectByType<T>( includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude );
#elif UNITY_2022_3_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>( includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude );
#else
            return UnityEngine.Object.FindObjectOfType<T>( includeInactive );
#endif
        }

        /// <summary> Calling FindObjectS OfType OfType or FindObjectS ByType depending on unity version </summary>
        public static T[] FindSceneObjects<T>( bool includeInactive = false ) where T : UnityEngine.Object
        {
#if UNITY_6000_4_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>( includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude );
#elif UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>( includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None );
#else
            return UnityEngine.Object.FindObjectsOfType<T>( includeInactive );
#endif
        }

        /// <summary> Rotates between directions, preserving the legacy half-turn axis on Unity 6.6 and newer </summary>
        public static Quaternion FromToRotation( Vector3 from, Vector3 to )
        {
#if UNITY_6000_6_OR_NEWER
            from.Normalize();
            to.Normalize();

            if( from == Vector3.zero || to == Vector3.zero ) return Quaternion.identity;

            float dot = Mathf.Clamp( Vector3.Dot( from, to ), -1f, 1f );

            // Opposite directions do not define a unique rotation axis. Unity 6.6 changed its choice, which flips the mapping of mirrored bones
            if( dot < -1f + 0.000001f )
            {
                // Legacy convention: project world right onto the perpendicular plane, using world up when the direction is parallel to right
                Vector3 axis = Vector3.right - from * from.x;
                if( axis.sqrMagnitude < 0.000001f ) axis = Vector3.up - from * from.y;
                axis.Normalize();
                return new Quaternion( axis.x, axis.y, axis.z, 0f );
            }

            Vector3 cross = Vector3.Cross( from, to );
            return new Quaternion( cross.x, cross.y, cross.z, 1f + dot ).normalized;
#else
            return Quaternion.FromToRotation( from, to );
#endif
        }


        #region Physics Helper Methods


        /// <summary> Sets rigidbody2D.isKinematic or rigidbody2D.bodyType Kinematic / Dynamic depending on unity version </summary>
        public static void FsetIsKinematic( this Rigidbody2D rig2D, bool kinematic )
        {
#if UNITY_2022_3_OR_NEWER
            if( kinematic ) rig2D.bodyType = RigidbodyType2D.Kinematic; else rig2D.bodyType = RigidbodyType2D.Dynamic;
#else
            rig2D.isKinematic = kinematic;
#endif
        }

        /// <summary> True if rigidbody2D is kinematic </summary>
        public static bool FisKinematic( this Rigidbody2D rig2D )
        {
#if UNITY_2022_3_OR_NEWER
            if( rig2D.bodyType == RigidbodyType2D.Dynamic ) return false; else return true;
#else
            return rig2D.isKinematic;
#endif
        }

        /// <summary> In unity 6 using GetEntityId().GetHashCode(), in earlier versions using obj.GetInstanceID() </summary>
        public static int FGetObjectIDHash( this UnityEngine.Object obj )
        {
            if( obj == null ) return 0;

#if UNITY_6000_4_OR_NEWER
            return obj.GetEntityId().GetHashCode();
#else
            return obj.GetInstanceID();
#endif
        }


        #endregion


        #region Game input helper methods


        /// <summary> Right mouse button press for both - old and new input system </summary>
        public static bool IsRightMouseButtonHoldDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
            return Input.GetMouseButton( 1 );
#endif
        }

        /// <summary> Left mouse button press for both - old and new input system </summary>
        public static bool IsLeftMouseButtonHoldDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton( 0 );
#endif
        }

        /// <summary> Right mouse button down for both - old and new input system </summary>
        public static bool OnRightMouseButtonPress()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown( 1 );
#endif
        }

        /// <summary> Left mouse button down for both - old and new input system </summary>
        public static bool IsLeftMouseButtonJustPressedDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown( 0 );
#endif
        }

        /// <summary> Right mouse button up for both - old and new input system </summary>
        public static bool OnRightMouseButtonReleased()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp( 1 );
#endif
        }

        /// <summary> Left mouse button up for both - old and new input system </summary>
        public static bool OnLeftMouseButtonReleased()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp( 0 );
#endif
        }

        /// <summary> Middle mouse button press for both - old and new input system </summary>
        public static bool IsMiddleMouseButtonHoldDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.middleButton.isPressed;
#else
            return Input.GetMouseButton( 2 );
#endif
        }

        /// <summary> Middle mouse button up for both - old and new input system </summary>
        public static bool OnMiddleMouseButtonReleased()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.middleButton.wasReleasedThisFrame;
#else
            return Input.GetMouseButtonUp( 2 );
#endif
        }

        /// <summary> GetKeyDown for both - old and new input system </summary>
        public static bool IsKeyPressedThisFrame( KeyCode keyCode )
        {
#if ENABLE_INPUT_SYSTEM
            KeyControl key = GetInputSystemKeyControl(keyCode);
            return key != null && key.wasPressedThisFrame;
#else
            return Input.GetKeyDown( keyCode );
#endif
        }

        /// <summary> GetKey for both - old and new input system </summary>
        public static bool IsKeyPressed( KeyCode keyCode )
        {
#if ENABLE_INPUT_SYSTEM
            KeyControl key = GetInputSystemKeyControl(keyCode);
            return key != null && key.isPressed;
#else
            return Input.GetKey( keyCode );
#endif
        }

        /// <summary> GetKeyUp for both - old and new input system </summary>
        public static bool IsKeyReleasedThisFrame( KeyCode keyCode )
        {
#if ENABLE_INPUT_SYSTEM
            KeyControl key = GetInputSystemKeyControl(keyCode);
            return key != null && key.wasReleasedThisFrame;
#else
            return Input.GetKeyUp( keyCode );
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static KeyControl GetInputSystemKeyControl(KeyCode keyCode)
        {
            if (Keyboard.current == null || !TryGetInputSystemKey(keyCode, out Key inputSystemKey)) return null;
            return Keyboard.current[inputSystemKey];
        }

        private static bool TryGetInputSystemKey(KeyCode keyCode, out Key inputSystemKey)
        {
            string keyName;

            switch (keyCode)
            {
                case KeyCode.Return: keyName = "Enter"; break;
                case KeyCode.KeypadEnter: keyName = "NumpadEnter"; break;
                case KeyCode.Alpha0: keyName = "Digit0"; break;
                case KeyCode.Alpha1: keyName = "Digit1"; break;
                case KeyCode.Alpha2: keyName = "Digit2"; break;
                case KeyCode.Alpha3: keyName = "Digit3"; break;
                case KeyCode.Alpha4: keyName = "Digit4"; break;
                case KeyCode.Alpha5: keyName = "Digit5"; break;
                case KeyCode.Alpha6: keyName = "Digit6"; break;
                case KeyCode.Alpha7: keyName = "Digit7"; break;
                case KeyCode.Alpha8: keyName = "Digit8"; break;
                case KeyCode.Alpha9: keyName = "Digit9"; break;
                case KeyCode.Keypad0: keyName = "Numpad0"; break;
                case KeyCode.Keypad1: keyName = "Numpad1"; break;
                case KeyCode.Keypad2: keyName = "Numpad2"; break;
                case KeyCode.Keypad3: keyName = "Numpad3"; break;
                case KeyCode.Keypad4: keyName = "Numpad4"; break;
                case KeyCode.Keypad5: keyName = "Numpad5"; break;
                case KeyCode.Keypad6: keyName = "Numpad6"; break;
                case KeyCode.Keypad7: keyName = "Numpad7"; break;
                case KeyCode.Keypad8: keyName = "Numpad8"; break;
                case KeyCode.Keypad9: keyName = "Numpad9"; break;
                case KeyCode.KeypadPeriod: keyName = "NumpadPeriod"; break;
                case KeyCode.KeypadDivide: keyName = "NumpadDivide"; break;
                case KeyCode.KeypadMultiply: keyName = "NumpadMultiply"; break;
                case KeyCode.KeypadMinus: keyName = "NumpadMinus"; break;
                case KeyCode.KeypadPlus: keyName = "NumpadPlus"; break;
                case KeyCode.KeypadEquals: keyName = "NumpadEquals"; break;
                case KeyCode.LeftControl: keyName = "LeftCtrl"; break;
                case KeyCode.RightControl: keyName = "RightCtrl"; break;
                case KeyCode.LeftCommand:
                case KeyCode.LeftWindows: keyName = "LeftMeta"; break;
                case KeyCode.RightCommand:
                case KeyCode.RightWindows: keyName = "RightMeta"; break;
                case KeyCode.Print: keyName = "PrintScreen"; break;
                case KeyCode.Menu: keyName = "ContextMenu"; break;
                case KeyCode.Break: keyName = "Pause"; break;
                default: keyName = keyCode.ToString(); break;
            }

            return System.Enum.TryParse<Key>(keyName, true, out inputSystemKey);
        }
#endif


        /// <summary> Mouse position in screen pixels for both - old and new input system </summary>
        public static Vector3 GetMousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero;
#else
            return Input.mousePosition;
#endif
        }

        /// <summary> Jump button hold - the new input system uses Space or the gamepad north button (the project's joystick button 3 binding) </summary>
        public static bool IsJumpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return IsKeyPressed( KeyCode.Space ) || (Gamepad.current != null && Gamepad.current.buttonNorth.isPressed);
#else
            return Input.GetButton( "Jump" );
#endif
        }

        /// <summary> Jump button down - the new input system uses Space or the gamepad north button (the project's joystick button 3 binding) </summary>
        public static bool IsJumpPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return IsKeyPressedThisFrame( KeyCode.Space ) || (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame);
#else
            return Input.GetButtonDown( "Jump" );
#endif
        }

        /// <summary> Mouse wheel axis, using the default legacy sensitivity of 0.1 in the new input system </summary>
        public static float GetMouseScrollAxis( float inputSystemUnitsPerTick = 0f )
        {
#if ENABLE_INPUT_SYSTEM
            if( Mouse.current == null ) return 0f;
            if( inputSystemUnitsPerTick <= 0f )
            {
#if (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN) && !UNITY_2023_2_OR_NEWER
                inputSystemUnitsPerTick = 120f;
#else
                inputSystemUnitsPerTick = 1f;
#endif
            }

            return Mouse.current.scroll.ReadValue().y * (0.1f / inputSystemUnitsPerTick);
#else
            return Input.GetAxis( "Mouse ScrollWheel" );
#endif
        }

        /// <summary> Named axis lookup </summary>
        public static float GetAxis( string axisName )
        {
#if ENABLE_INPUT_SYSTEM
            switch( axisName )
            {
                case "Horizontal": return GetHorizontalAxis();
                case "Vertical": return GetVerticalAxis();
                case "Mouse X": return GetMouseXAxis();
                case "Mouse Y": return GetMouseYAxis();
                case "Mouse ScrollWheel": return GetMouseScrollAxis();
                default: throw new System.ArgumentException( "Unsupported input axis: " + axisName, "axisName" );
            }
#else
            return Input.GetAxis( axisName );
#endif
        }

        /// <summary> GetAxis Mouse X for both - old and new input system </summary>
        public static float GetMouseXAxis()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? (Mouse.current.delta.ReadValue().x * 0.035f) : 0f;
#else
            return Input.GetAxis( "Mouse X" );
#endif
        }

        /// <summary> GetAxis Mouse Y for both - old and new input system </summary>
        public static float GetMouseYAxis()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? (Mouse.current.delta.ReadValue().y * 0.035f) : 0f;
#else
            return Input.GetAxis( "Mouse Y" );
#endif
        }

        /// <summary> GetAxis Horizontal for both - old and new input system - for new input system it simply checks A/D/arrow keys </summary>
        public static float GetHorizontalAxis()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null) return 0f;

            float horizontal = 0f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) horizontal -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) horizontal += 1f;
            return horizontal;
#else
            return Input.GetAxis( "Horizontal" );
#endif
        }

        /// <summary> GetAxis Vertical for both - old and new input system - for new input system it simply checks W/S/arrow keys </summary>
        public static float GetVerticalAxis()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current == null) return 0f;

            float vertical = 0f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) vertical -= 1f;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) vertical += 1f;
            return vertical;
#else
            return Input.GetAxis( "Vertical" );
#endif
        }

        #endregion


        #region Editor Only Related

        public static System.Reflection.Assembly[] GetCurrentAssemblies()
        {
#if UNITY_6000_7_OR_NEWER
            var assem = UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
            var asArray = new System.Reflection.Assembly[assem.Count];
            for( int i = 0; i < assem.Count; i++ ) asArray[i] = assem[i] as System.Reflection.Assembly;
            return asArray;
#else
            return System.AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

#if UNITY_EDITOR

        public static void ImportPackage( string path, bool interactive )
        {
#if UNITY_6000_7_OR_NEWER
            UnityEditor.AssetPackage.Package.Import( path, interactive );
#else
            AssetDatabase.ImportPackage( path, interactive );
#endif
        }

#endif

        #region OnOpenAssetAttribute template 

        //[OnOpenAssetAttribute(1)]
        //#if UNITY_6000_4_OR_NEWER
        //        public static bool OpenDesignerScriptableFile(EntityId instanceID, int line)
        //        {
        //            UnityEngine.Object obj = EditorUtility.EntityIdToObject(instanceID);
        //#else
        //        public static bool OpenDesignerScriptableFile(int instanceID, int line)
        //        {
        //            UnityEngine.Object obj = EditorUtility.InstanceIDToObject(instanceID);
        //#endif

        #endregion

        #endregion


    }

}
