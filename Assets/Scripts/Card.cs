using System;
using System.Threading;
using Cysharp.Threading.Tasks; // 🌟UniTaskのインポート
using UnityEngine;

public class Card : MonoBehaviour
{
    // このカードが持っているデータ
    public CardData Data { get; private set; }

    private Sprite _faceSprite;
    private Sprite _backSprite;
    private SpriteRenderer _sr;

    private bool _isSelected = false;
    private Vector3 _originalLocalPos; // 選択解除時に戻る場所

    [Header("Glow Effect")]
    [SerializeField] private GameObject glowObject; // エフェクトオブジェクトをUnity上で紐付ける枠

    // 🌟移動中かどうかを判定するフラグ（移動中にクリックされるのを防ぐなどの用途に）
    public bool IsMoving { get; private set; } = false;

    public void Initialize(CardData data, Sprite face, Sprite back)
    {
        this.Data = data;
        this._faceSprite = face;
        this._backSprite = back;

        _sr = GetComponent<SpriteRenderer>();

        // 最初は山札なので裏向き
        SetFaceUp(false);

        // 初期状態（山札の中など）ではエフェクトを非表示にする
        SetGlow(false);
    }

    // 表裏を切り替える関数
    public void SetFaceUp(bool isFaceUp)
    {
        if (_sr == null) _sr = GetComponent<SpriteRenderer>();
        _sr.sprite = isFaceUp ? _faceSprite : _backSprite;
    }

    private void OnMouseDown()
    {
        // 🌟移動中はクリックを受け付けない（バグ防止）
        if (IsMoving) return;

        // GameManagerに通知
        GameManager gm = GameObject.FindAnyObjectByType<GameManager>(); if (gm != null)
        {
            gm.OnCardSelected(this);
            Debug.Log($"Card clicked: {Data.id}", this);
        }
    }

    public void SetSelected(bool selected)
    {
        if (_isSelected == selected) return;

        _isSelected = selected;

        if (_isSelected)
        {
            // 選択されたら少し上にずらす
            _originalLocalPos = transform.localPosition;
            transform.localPosition += new Vector3(0, 0.3f, 0);
            _sr.color = Color.yellow; // 視覚的に分かりやすく色を変える（任意）
        }
        else
        {
            // 解除されたら元の位置に戻す
            transform.localPosition = _originalLocalPos;
            _sr.color = Color.white;
        }
    }

    public void SetGlow(bool active)
    {
        if (glowObject != null)
        {
            glowObject.SetActive(active);
        }
    }

    /// <summary>
    /// 🌟追加：指定した世界座標（World Position）へ滑らかに移動させる非同期関数
    /// </summary>
    /// <param name="targetWorldPosition">移動先のワールド座標</param>
    /// <param name="duration">移動にかける時間（秒）</param>
    /// <param name="cancellationToken">GameObjectが破棄された時にタスクを安全に止めるためのトークン</param>
    public async UniTask MoveToPositionAsync(Vector3 targetWorldPosition, float duration, CancellationToken cancellationToken = default)
    {
        IsMoving = true;
        Vector3 startPosition = transform.position;
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            // 途中でゲーム終了やシーン遷移、オブジェクト破棄があったら安全に抜ける
            cancellationToken.ThrowIfCancellationRequested();

            elapsedTime += Time.deltaTime;
            float t = elapsedTime / duration;

            // イージング（SmoothStepで加速・減速を滑らかに）
            t = Mathf.SmoothStep(0f, 1f, t);

            // ワールド座標を補間
            transform.position = Vector3.Lerp(startPosition, targetWorldPosition, t);

            // Unityの通常のUpdateタイミングまで1フレーム待機
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        // 最後に確実に目標座標に合わせる
        transform.position = targetWorldPosition;
        IsMoving = false;
    }
}