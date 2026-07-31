using UnityEngine;

// 責任：手首（非利き手など）に追従するメニューから原子をスポーンさせる
public class WristAtomSpawner : MonoBehaviour
{
    [Header("Anchor & Panel")]
    [Tooltip("手首やコントローラーのアンカー（例：OVRCameraRigのLeftHandAnchor）")]
    [SerializeField] private Transform _leftHandAnchor;
    
    [Tooltip("UIやボタンが乗っているパネル本体")]
    [SerializeField] private GameObject _menuPanel;

    [Tooltip("アンカーからのオフセット（位置調整用）")]
    [SerializeField] private Vector3 _offset = new Vector3(0, 0.1f, 0);

    [Tooltip("手首に対する角度調整用（例: X=45 などで押しやすい角度に）")]
    [SerializeField] private Vector3 _rotationOffset = Vector3.zero;

    [Tooltip("Poke Interactable用のSurface（Canvasと同じ位置・回転に同期させます）")]
    [SerializeField] private Transform _pokeSurface;

    [Header("Spawning")]
    [Tooltip("スポーンさせる原子のプレハブ（[0]=C, [1]=H, [2]=O などを想定）")]
    [SerializeField] private GameObject[] _atomPrefabs;

    [Tooltip("パネルの前方どのくらいの位置にスポーンさせるか")]
    [SerializeField] private float _spawnDistance = 0.2f;

    private void Update()
    {
        if (_leftHandAnchor != null && _menuPanel != null)
        {
            // パネルを手のアンカーに追従させる
            _menuPanel.transform.position = _leftHandAnchor.position + _leftHandAnchor.TransformDirection(_offset);
            
            // 手首の回転にオフセットを加えることで押しやすい角度に調整する
            _menuPanel.transform.rotation = _leftHandAnchor.rotation * Quaternion.Euler(_rotationOffset);

            // Surfaceが存在する場合は、Canvasと同じ位置・回転に同期させる
            if (_pokeSurface != null)
            {
                _pokeSurface.position = _menuPanel.transform.position;
                _pokeSurface.rotation = _menuPanel.transform.rotation;
            }
        }
    }

    // UIボタンのOnClickなどから呼び出す公開メソッド
    public void SpawnAtom(int elementIndex)
    {
        if (_atomPrefabs == null || elementIndex < 0 || elementIndex >= _atomPrefabs.Length)
        {
            Debug.LogWarning("【WristAtomSpawner】無効なelementIndexが指定されました。");
            return;
        }

        GameObject prefab = _atomPrefabs[elementIndex];
        if (prefab != null && _menuPanel != null)
        {
            // パネルの上方向(up)や前方(forward)など、UIの向きに応じてスポーン位置を調整
            Vector3 spawnPos = _menuPanel.transform.position + _menuPanel.transform.forward * _spawnDistance;
            Instantiate(prefab, spawnPos, Quaternion.identity);
        }
    }
}
