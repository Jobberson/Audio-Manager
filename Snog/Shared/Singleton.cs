using UnityEngine;

namespace Snog.Shared
{
    public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
    {
        private static T instance;

        public static T Instance
        {
            get
            {
                if (instance == null)
                {
    #if UNITY_2023_2_OR_NEWER
                    instance = FindAnyObjectByType<T>();
    #else
                    instance = FindObjectOfType<T>();
    #endif

                    if (instance == null)
                    {
                        Debug.LogError($"No instance of {typeof(T)} found in the scene.");
                    }
                }

                return instance;
            }
        }

        protected virtual void Awake()
        {
            if (instance == null)
            {
                instance = this as T;
                DontDestroyOnLoad(gameObject);
            }
            else if (instance != this)
            {
                Destroy(gameObject);
            }
        }
    }
}