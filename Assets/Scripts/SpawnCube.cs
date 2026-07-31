using UnityEngine;

public class SpawnCube : MonoBehaviour
{
    public GameObject obj;

    public void OnXROnClick()
    {
        obj.SetActive(false);
    }
}
