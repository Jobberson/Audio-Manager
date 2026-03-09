using UnityEngine;

namespace Snog.Shared
{
    /// <summary>
    /// Thread-safe singleton pattern for MonoBehaviours.
    /// Automatically persists across scene loads via DontDestroyOnLoad.
    /// </summary>
    /// <typeparam name="T">The MonoBehaviour type to make a singleton.</typeparam>
    public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance;
        private static readonly object lockObject = new();
        private static bool applicationIsQuitting = false;
        private static bool instanceDestroyed = false;

        /// <summary>
        /// Gets the singleton instance. Creates one if it doesn't exist.
        /// Returns null if application is quitting to prevent object creation during shutdown.
        /// </summary>
        public static T Instance
        {
            get
            {
                // Don't create new instances during application shutdown
                if (applicationIsQuitting)
                {
                    Debug.LogWarning($"[Singleton] Instance of '{typeof(T)}' requested during application quit. Returning null.");
                    return null;
                }

                // Thread-safe singleton pattern
                lock (lockObject)
                {
                    // If instance was destroyed but we're not quitting, we can create a new one
                    if (instance == null && !instanceDestroyed)
                    {
                        // Try to find existing instance in scene
                        instance = FindAnyObjectByType<T>();

                        if (instance == null)
                        {
                            // Create new GameObject with singleton component
                            GameObject singletonObject = new();
                            instance = singletonObject.AddComponent<T>();
                            singletonObject.name = $"{typeof(T).Name} (Singleton)";

                            Debug.Log($"[Singleton] Created new instance of {typeof(T).Name}");
                        }
                    }

                    return instance;
                }
            }
        }

        /// <summary>
        /// Returns true if a singleton instance currently exists.
        /// Useful for checking existence without triggering instance creation.
        /// </summary>
        public static bool HasInstance => instance != null;

        /// <summary>
        /// Called when the MonoBehaviour is created.
        /// Enforces singleton pattern and sets up persistence.
        /// </summary>
        protected virtual void Awake()
        {
            lock (lockObject)
            {
                if (instance == null)
                {
                    instance = this as T;
                    instanceDestroyed = false;
                    
                    // CRITICAL: Persist across scene loads
                    DontDestroyOnLoad(gameObject);
                    
                    Debug.Log($"[Singleton] {typeof(T).Name} initialized and set to persist across scenes.");
                    
                    OnSingletonAwake();
                }
                else if (instance != this)
                {
                    // Duplicate instance detected - destroy it
                    Debug.LogWarning($"[Singleton] Duplicate instance of {typeof(T).Name} found. Destroying duplicate.");
                    Destroy(gameObject);
                }
            }
        }

        /// <summary>
        /// Called when this singleton is first initialized.
        /// Override this instead of Awake() in derived classes.
        /// </summary>
        protected virtual void OnSingletonAwake()
        {
            // Override in derived classes for initialization
        }

        /// <summary>
        /// Called when the MonoBehaviour is destroyed.
        /// Clears the singleton reference if this was the active instance.
        /// </summary>
        protected virtual void OnDestroy()
        {
            lock (lockObject)
            {
                if (instance == this)
                {
                    instance = null;
                    instanceDestroyed = true;
                    
                    OnSingletonDestroy();
                    
                    Debug.Log($"[Singleton] {typeof(T).Name} instance destroyed.");
                }
            }
        }

        /// <summary>
        /// Called when this singleton is being destroyed.
        /// Override this instead of OnDestroy() in derived classes.
        /// </summary>
        protected virtual void OnSingletonDestroy()
        {
            // Override in derived classes for cleanup
        }

        /// <summary>
        /// Called when the application is quitting.
        /// Prevents new instance creation during shutdown.
        /// </summary>
        protected virtual void OnApplicationQuit()
        {
            applicationIsQuitting = true;
        }

        /// <summary>
        /// Manually destroys the singleton instance.
        /// Use with caution - typically only needed for testing or specific cleanup scenarios.
        /// </summary>
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