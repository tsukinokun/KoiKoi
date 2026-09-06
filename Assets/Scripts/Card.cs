using System;
using System.Threading;
using Cysharp.Threading.Tasks; // 🌟UniTaskのインポート
using UnityEngine;
using UnityEngine.Rendering;

public class Card : MonoBehaviour
{
    // このカードが持っているデータ
    public CardData Data { get; private set; }

    private Sprite _faceSprite;
    private Sprite _backSprite;
    private SpriteRenderer _sr;
    private SortingGroup _sortingGroup;

    private bool _isSelected = false;
    private Vector3 _originalLocalPos; // 選択解除時に戻る場所

    [Header("Glow Effect")]
    [SerializeField] private GameObject glowObject; // エフェクトオブジェクトをUnity上で紐付ける枠

    // 🌟移動中かどうかを判定するフラグ（移動中にクリックされるのを防ぐなどの用途に）
    public bool IsMoving { get; private set; } = false;

    // カードの描画順
    // （畳=0 ＜ 裏向き=10 ＜ 表向き=20 ＜ 出した札・めくった札=30 ＜ カットイン=100 ＜ 各種ウィンドウ=150）
    private const int FaceDownSortingOrder = 10;
    private const int FaceUpSortingOrder = 20;
    private const int OnTopSortingOrder = 30;

    private bool _isFaceUp = false;
    private bool _isOnTop = false;

    // カードがクリックされたことを通知するイベント（GameManagerへの直接参照を持たないための疎結合化）
    public static event Action<Card> Clicked;

    public void Initialize(CardData data, Sprite face, Sprite back)
    {
        this.Data = data;
        this._faceSprite = face;
        this._backSprite = back;

        _sr = GetComponent<SpriteRenderer>();

        // 🌟 場や手札のコンテナが持つSortingGroupに描画順を乗っ取られないようにする。
        //    これがないと「表向き/裏向き」の描画順が入れ物の中でしか効かなくなる。
        _sortingGroup = GetComponent<SortingGroup>();
        if (_sortingGroup != null) _sortingGroup.sortAtRoot = true;

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

        _isFaceUp = isFaceUp;
        ApplySortingOrder();
    }

    /// <summary>
    /// 🌟出した札・めくった札を、既にある場札より手前に描画させる（移動・重ね演出の間だけ有効にする）
    /// </summary>
    public void SetOnTop(bool onTop)
    {
        _isOnTop = onTop;
        ApplySortingOrder();
    }

    /// <summary>
    /// 表裏と「出した札かどうか」から描画順を決める。
    /// どの入れ物（場・手札など）に入っていても、表向きは裏向きより手前・出した札は場札より手前になる。
    /// </summary>
    private void ApplySortingOrder()
    {
        if (_sortingGroup == null) _sortingGroup = GetComponent<SortingGroup>();
        if (_sortingGroup == null) return;

        if (!_isFaceUp)
        {
            _sortingGroup.sortingOrder = FaceDownSortingOrder;
        }
        else
        {
            _sortingGroup.sortingOrder = _isOnTop ? OnTopSortingOrder : FaceUpSortingOrder;
        }
    }

    private void OnMouseDown()
    {
        // 🌟移動中はクリックを受け付けない（バグ防止）
        if (IsMoving) return;

        Debug.Log($"Card clicked: {Data.id}", this);
        Clicked?.Invoke(this);
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
    /// 🌟追加：くるっと回転してめくるアニメーション（0°→90°でスプライトを差し替え→0°に戻す）
    /// </summary>
    public async UniTask FlipAsync(bool isFaceUp, float duration, CancellationToken cancellationToken = default)
    {
        IsMoving = true;

        float half = duration / 2f;

        float elapsed = 0f;
        while (elapsed < half)
        {
            cancellationToken.ThrowIfCancellationRequested();
            elapsed += Time.deltaTime;
            float angle = Mathf.SmoothStep(0f, 90f, elapsed / half);
            transform.localRotation = Quaternion.Euler(0f, angle, 0f);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }
        transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

        SetFaceUp(isFaceUp);

        elapsed = 0f;
        while (elapsed < half)
        {
            cancellationToken.ThrowIfCancellationRequested();
            elapsed += Time.deltaTime;
            float angle = Mathf.SmoothStep(90f, 0f, elapsed / half);
            transform.localRotation = Quaternion.Euler(0f, angle, 0f);
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }
        transform.localRotation = Quaternion.identity;

        IsMoving = false;
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

    /// <summary>
    /// 🌟追加：指定したローカル座標（Local Position）へ滑らかに移動させる非同期関数
    /// </summary>
    public async UniTask MoveToLocalPositionAsync(Vector3 targetLocalPosition, float duration, CancellationToken cancellationToken = default)
    {
        IsMoving = true;
        Vector3 startPosition = transform.localPosition; // 💡localPosition を使用
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            elapsedTime += Time.deltaTime;
            float t = elapsedTime / duration;

            // イージング（SmoothStep）
            t = Mathf.SmoothStep(0f, 1f, t);

            // 💡ローカル座標を補間
            transform.localPosition = Vector3.Lerp(startPosition, targetLocalPosition, t);

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        // 最後に確実に目標のローカル座標に合わせる
        transform.localPosition = targetLocalPosition;
        IsMoving = false;
    }
}