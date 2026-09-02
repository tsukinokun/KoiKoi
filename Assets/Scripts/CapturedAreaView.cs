using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks; // 🌟UniTaskを使用するために追加

/// <summary>
/// 獲得した札をタイプ（光・種・短冊・カス）ごとに仕分け、整列させるビューコンポーネント
/// </summary>
public class CapturedAreaView : MonoBehaviour
{
    [Header("Captured Category Parents")]
    [SerializeField] private Transform hikariParent;
    [SerializeField] private Transform taneParent;
    [SerializeField] private Transform tanParent;
    [SerializeField] private Transform kasuParent;

    /// <summary>
    /// 4つのカテゴリ（光・種・短冊・カス）に仕分けられたカードをすべて列挙する
    /// </summary>
    public IEnumerable<Card> Cards
    {
        get
        {
            Transform[] categoryParents = { hikariParent, taneParent, tanParent, kasuParent };
            foreach (Transform categoryParent in categoryParents)
            {
                if (categoryParent == null) continue;
                foreach (Transform child in categoryParent)
                {
                    Card card = child.GetComponent<Card>();
                    if (card != null && card.Data != null)
                    {
                        yield return card;
                    }
                }
            }
        }
    }

    [Header("Layout Settings")]
    [SerializeField] private float xSpacing = 0.3f; // 横にずらす幅
    [SerializeField] private Vector3 cardScale = new Vector3(0.5f, 0.5f, 1f); // 獲得札の大きさ

    [Tooltip("親の中心から、どれくらい左側から並べ始めるか（マイナス値で左へ）")]
    [SerializeField] private float xStartOffset = -1.5f; // 🌟ここをインスペクターで調整して左端に合わせます

    [Header("Animation Settings")]
    [SerializeField] private float moveDuration = 0.4f; // 🌟獲得エリアへ滑り込む時間
    public float MoveDuration => moveDuration; // 外部から時間を参照できるようにする

    /// <summary>
    /// カードをタイプに応じて適切な親トランスフォームに割り振り、滑らかに整列させる
    /// </summary>
    public void AddCard(Card card, string cardType)
    {
        if (card == null) return;

        string typeKey = cardType.ToLower();
        Transform targetParent = null;

        if (typeKey == "hikari") targetParent = hikariParent;
        else if (typeKey == "tane") targetParent = taneParent;
        else if (typeKey == "tan" || typeKey == "tanzaku") targetParent = tanParent;
        else targetParent = kasuParent;

        if (targetParent == null)
        {
            Debug.LogWarning($"獲得エリアの親トランスフォームが未設定です。タイプ: {cardType}");
            targetParent = this.transform;
        }

        // 1️⃣ 🌟【ワープ防止】親が変わる前の「現在の世界的な位置（World Position/Rotation）」をキープ
        Vector3 currentWorldPos = card.transform.position;
        Quaternion currentWorldRot = card.transform.rotation;

        // 親を変更する（worldPositionStays: true にしてUnity標準のワープ防止機能も効かせる）
        card.transform.SetParent(targetParent, worldPositionStays: true);

        // 2️⃣ 🌟サイズ調整
        // (親が変わって scale が狂うのを防ぐため、SetParentの「後」にローカルスケールを設定)
        card.transform.localScale = cardScale;

        // 3️⃣ 獲得エリア内での最終的な目標ローカル座標を計算
        int childCount = targetParent.childCount;
        float xPosition = xStartOffset + ((childCount - 1) * xSpacing);

        // Z軸は重ね順（新しく獲得したカードほど手前に表示されるように -0.01f を掛ける）
        Vector3 targetLocalPos = new Vector3(xPosition, 0, -0.01f * childCount);

        // 4️⃣ 🌟回転は一瞬で正面（identity）に向き直るのではなく、傾きも滑らかに戻るように補間移動に任せる
        // (カード側の Move メソッドが localPosition のみ対応している場合を考慮し、
        //  回転とZ軸の初期位置だけを移動前に一度安全に処理します)

        // 回転の初期値を世界の回転からローカルに変換して合わせる
        card.transform.localRotation = Quaternion.Inverse(targetParent.rotation) * currentWorldRot;

        // 5️⃣ 🌟非同期で目的地へぬるっと移動（回転も正面に戻す）
        // ※もし Card クラスの MoveToLocalPositionAsync が回転の補間に対応していない場合は、
        // ここで `card.transform.localRotation = Quaternion.identity;` を別途演出しても良いですが、
        // 移動と同時にまっすぐ戻るのが一番綺麗です。
        card.transform.localRotation = Quaternion.identity; // 一旦シンプルに正面に向けます

        // カード単体でターゲットのローカル座標へ滑らかに移動開始！
        card.MoveToLocalPositionAsync(targetLocalPos, moveDuration, card.GetCancellationTokenOnDestroy()).Forget();
    }
}