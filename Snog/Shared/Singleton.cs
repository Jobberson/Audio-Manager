using UnityEngine;

namespace Snog.Shared
{
    /// <summary>
    /// Thread-safe singleton pattern for MonoBehaviours.
    /// Automatically persists across scene loads via DontDestroyOnLoad.
    /// </summary>
    public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance;
        private static readonly object lockObject = new();
        private static bool applicationIsQuitting = false;
        private static bool instanceDestroyed = false;

        public static T Instance
        {
            get
            {
                if (applicationIsQuitting)
                {
                    // Guard: never spam the player log in shipping builds during quit.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.LogWarning($"[Singleton] Instance of '{typeof(T)}' requested during application quit. Returning null.");
#endif
                    return null;
                }

                lock (lockObject)
                {
                    if (instance == null && !instanceDestroyed)
                    {
                        instance = FindAnyObjectByType<T>();

                        if (instance == null)
                        {
                            GameObject singletonObject = new();
                            instance = singletonObject.AddComponent<T>();
                            singletonObject.name = $"{typeof(T).Name} (Singleton)";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            Debug.Log($"[Singleton] Created new instance of {typeof(T).Name}");
#endif
                        }
                    }

                    return instance;
                }
            }
        }

        public static bool HasInstance => instance != null;

        protected virtual void Awake()
        {
            lock (lockObject)
            {
                if (instance == null)
                {
                    instance = this as T;
                    instanceDestroyed = false;
                    DontDestroyOnLoad(gameObject);

                    // Lifecycle log: editor and development builds only.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[Singleton] {typeof(T).Name} initialized and set to persist across scenes.");
#endif

                    OnSingletonAwake();
                }
                else if (instance != this)
                {
                    // Always warn on duplicates — this usually means a setup error.
                    Debug.LogWarning($"[Singleton] Duplicate instance of {typeof(T).Name} found. Destroying duplicate.");
                    Destroy(gameObject);
                }
            }
        }

        protected virtual void OnSingletonAwake() { }

        protected virtual void OnDestroy()
        {
            lock (lockObject)
            {
                if (instance == this)
                {
                    instance = null;
                    instanceDestroyed = true;
                    OnSingletonDestroy();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[Singleton] {typeof(T).Name} instance destroyed.");
#endif
                }
            }
        }

        protected virtual void OnSingletonDestroy() { }

        protected virtual void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        public static void DestroySingleton()
        {
            lock (lockObject)
            {
                if (instance != null)
                {
                    Destroy(instance.gameObject);
                    instance = null;
                    instanceDestroyed = true;
                }
            }
        }
    }
}
