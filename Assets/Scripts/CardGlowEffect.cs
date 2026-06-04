using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class CardGlowEffect : MonoBehaviour
{
    [Header("Animation Settings")]
    [SerializeField] private float fadeSpeed = 2.0f;     // 補間スピード
    [SerializeField] private float maxAlpha = 1.0f;      // アルファの最大値 (0.0 ~ 1.0)
    [SerializeField] private float minAlpha = 0.0f;      // アルファの最小値 (0.0 ~ 1.0)

    private SpriteRenderer _spriteRenderer;
    private float _timeCounter;

    void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
    }

    void OnEnable()
    {
        // アクティブになった瞬間はアルファを最小値からスタート
        _timeCounter = 0f;
        SetAlpha(minAlpha);
    }

    void Update()
    {
        if (_spriteRenderer == null) return;

        // 時間を進める
        _timeCounter += Time.deltaTime * fadeSpeed;

        // Mathf.PingPong を使って 0 ~ 1 の間を線形に往復（リニア補間）させる
        float pingPong = Mathf.PingPong(_timeCounter, 1.0f);

        // 最小値〜最大値の間で線形補間 (Lerp)
        float currentAlpha = Mathf.Lerp(minAlpha, maxAlpha, pingPong);

        SetAlpha(currentAlpha);
    }

    private void SetAlpha(float alpha)
    {
        Color color = _spriteRenderer.color;
        color.a = alpha;
        _spriteRenderer.color = color;
    }
}