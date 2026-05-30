using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.U2D;
using System.Linq;

public enum TurnState
{
    PlayerTurn,
    NPCTurn,
    CheckingMatch
}

public class GameManager : MonoBehaviour
{
    private TurnState _currentState = TurnState.PlayerTurn;

    public SpriteAtlas cardAtlas;
    public GameObject cardPrefab;

    public Transform playerHandParent; // プレイヤー手札の親
    public Transform enemyHandParent;  // 相手手札の親
    public Transform fieldParent;      // 場札の親

    [Header("Player Captured Areas")]
    public Transform pHikariParent;
    public Transform pTaneParent;
    public Transform pTanParent;
    public Transform pKasuParent;

    [Header("Enemy Captured Areas")]
    public Transform eHikariParent;
    public Transform eTaneParent;
    public Transform eTanParent;
    public Transform eKasuParent;

    // これが「山札」の実体です
    private List<Card> _deck = new List<Card>();

    // 現在選択されているカードの参照
    private Card _currentSelectedCard;

    [Header("UI Managers")]
    [SerializeField] private YakuWindowManager yakuWindowManager;

    void Start()
    {
        // JSONを読み込み、48枚を生成して山札に入れる
        CreateDeck();

        // 山札をシャッフルする
        Shuffle();

        // カードを配る
        DealInitialCards();
    }

    void CreateDeck()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "JSON", "cards_master.json");
        string jsonText = File.ReadAllText(path);

        string wrappedJson = "{\"cards\":" + jsonText + "}";

        // 成形した wrappedJson を読み込む
        CardList cardList = JsonUtility.FromJson<CardList>(wrappedJson);

        Sprite backSprite = cardAtlas.GetSprite("Card_Back");

        // あとはそのまま
        if (cardList == null || cardList.cards == null)
        {
            Debug.LogError("JSONのパースに失敗しました。形式を確認してください。");
            return;
        }

        foreach (var data in cardList.cards)
        {
            // 生成
            GameObject go = Instantiate(cardPrefab);
            Sprite faceSprite = cardAtlas.GetSprite(data.id);
            Debug.Log(data.id);

            Card card = go.GetComponent<Card>();
            // データ、表面、裏面をセットして初期化
            card.Initialize(data, faceSprite, backSprite);

            // 山札の定位置（左側など）に移動させて裏向きにする
            go.transform.position = new Vector3(-5f, 0, 0);
            card.SetFaceUp(false);

            // リストに溜める
            _deck.Add(card);
        }
        Debug.Log($"山札に {_deck.Count} 枚準備しました。");
    }

    void Shuffle()
    {
        // フィッシャー–イェーツのシャッフル
        for (int i = _deck.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Card temp = _deck[i];
            _deck[i] = _deck[j];
            _deck[j] = temp;
        }
        Debug.Log("シャッフル完了！");
    }

    // 最初の手札・場札を配る
    void DealInitialCards()
    {
        // 場札を配る
        for (int i = 0; i < 8; i++) transferCard(fieldParent, true, i);
        // ★ここで配り終わった場札を綺麗に並べる
        RearrangeFieldCards();

        // プレイヤーの手札を配る
        for (int i = 0; i < 8; i++) transferCard(playerHandParent, true, i);

        // 相手の手札を配る（裏向き）
        for (int i = 0; i < 8; i++) transferCard(enemyHandParent, false, i);
    }

    // 場札を現在の枚数に応じてグリッド状にきれいに並べ直す関数
    void RearrangeFieldCards()
    {
        float xSpacing = 1.2f;
        float ySpacing = 1.5f; // 行間を少し広げて見やすく調整

        // 場札の総数に応じて、1行あたりの列数を動的に決める（最低4列、枚数が増えたら5列、6列と横に広げる）
        int childCount = fieldParent.childCount;
        int maxColumns = 4;
        if (childCount > 8) maxColumns = Mathf.CeilToInt(childCount / 2f); // 2行を維持して横に広げる場合

        // もし横幅を広げず、3行、4行と下に伸ばしたい場合は maxColumns = 4 のままでOKです。

        int index = 0;
        foreach (Transform child in fieldParent)
        {
            Card card = child.GetComponent<Card>();
            if (card == null) continue;

            // 動的に計算された列数ベースで位置を決める
            int column = index % maxColumns;
            int row = index / maxColumns;

            // 中央揃えにするためのオフセット計算
            float x = (column - (maxColumns - 1) / 2f) * xSpacing;
            float y = -(row * ySpacing) + (ySpacing / 2f); // 行（row）が増えても正しく下にズレるように修正

            // ★重要★ Unityの2D描画順を保つため、インデックスが大きい（後から来た）札ほど
            // Z軸を手前（カメラ側、つまりマイナス方向）に出す
            card.transform.localPosition = new Vector3(x, y, -0.05f * index);
            card.transform.localRotation = Quaternion.identity;

            index++;
        }
    }

    // 山札から指定の場所にカードを物理的に移動させる
    void transferCard(Transform targetParent, bool isFaceUp, int index)
    {
        if (_deck.Count == 0) return;

        // リスト（山札）の最後から1枚取り出す
        Card card = _deck[_deck.Count - 1];
        _deck.RemoveAt(_deck.Count - 1);

        // 親を指定の場所（FieldParentなど）に付け替える
        card.transform.SetParent(targetParent);

        // 表裏をセット
        card.SetFaceUp(isFaceUp);

        if (targetParent == playerHandParent || targetParent == enemyHandParent)
        {
            // 【手札：扇形に並べる】
            bool isEnemy = (targetParent == enemyHandParent);

            float radius = 12.0f;     // 円の半径
            float angleStep = 5.0f;   // カード間の角度

            // プレイヤーは90度（真上）、敵は270度（真下）を基準にする
            float baseAngle = isEnemy ? 270.0f : 90.0f;

            // 敵の場合は並び順を反転させる
            float currentAngle = baseAngle + (index - 3.5f) * angleStep * (isEnemy ? 1 : -1);
            float rad = currentAngle * Mathf.Deg2Rad;

            float x = Mathf.Cos(rad) * radius;
            // 敵は半径分「上」へ、プレイヤーは「下」へオフセット
            float y = (Mathf.Sin(rad) * radius) + (isEnemy ? radius : -radius);

            card.transform.localPosition = new Vector3(x, y, -0.01f * index);

            // 回転：敵なら下を向くように調整
            float rotationOffset = isEnemy ? 270.0f : 90.0f;
            card.transform.localRotation = Quaternion.Euler(0, 0, currentAngle - rotationOffset);
        }
        else
        {
            // ここでは親の付け替えと表裏のセットだけ行い、
            // 座標リセットは配り終わった後の RearrangeFieldCards に任せる
            card.transform.localPosition = Vector3.zero;
            card.transform.localRotation = Quaternion.identity;
        }
    }

    // 獲得札エリアかどうかを判定する補助関数
    bool IsCapturedArea(Transform t)
    {
        return t == pHikariParent || t == pTaneParent || t == pTanParent || t == pKasuParent ||
               t == eHikariParent || t == eTaneParent || t == eTanParent || t == eKasuParent;
    }

    public void OnCardSelected(Card clickedCard)
    {
        // ガード1: 引数のカード自体がヌルなら処理しない
        if (clickedCard == null) return;

        // ガード2: プレイヤーのターン以外は一切のクリックを無視
        if (_currentState != TurnState.PlayerTurn) return;

        // ガード3: カードのデータがバインドされていなければ処理しない
        if (clickedCard.Data == null)
        {
            Debug.LogError($"クリックされたカード {clickedCard.name} のDataが割り当てられていません！");
            return;
        }

        Transform currentParent = clickedCard.transform.parent;

        // 【フェーズ1: 自分の手札をクリックした時】
        if (currentParent == playerHandParent)
        {
            // ★すでにこのカードが選択されている状態でもう一度クリックされた場合（ダブルクリック扱い：場に捨てる）
            if (_currentSelectedCard == clickedCard)
            {
                Debug.Log($"手札の再クリックを検知: {clickedCard.Data.month}月を場に捨てます。");

                // 選択ポインタをクリア
                _currentSelectedCard = null;

                // カードの選択状態を解除して場に移動
                clickedCard.SetSelected(false);
                clickedCard.transform.SetParent(fieldParent);
                clickedCard.SetFaceUp(true); // 表を向ける

                // 場札を綺麗に並び替える
                RearrangeFieldCards();

                // 自分の山札めくりフェーズへ移行
                StartCoroutine(DrawFromDeckRoutine(true));
                return;
            }

            // 別の手札が選択されていたら古い方の選択を解除
            if (_currentSelectedCard != null)
            {
                _currentSelectedCard.SetSelected(false);
            }

            // 新しく手札を選択
            _currentSelectedCard = clickedCard;
            _currentSelectedCard.SetSelected(true);
            Debug.Log($"手札を選択しました: {_currentSelectedCard.Data.month}月 ({_currentSelectedCard.Data.type})");
        }
        // 【フェーズ2: 手札を選択した状態で、場札をクリックした時】
        else if (currentParent == fieldParent && _currentSelectedCard != null)
        {
            // 安全のため、選択中の手札のデータもヌルチェック
            if (_currentSelectedCard.Data == null)
            {
                _currentSelectedCard.SetSelected(false);
                _currentSelectedCard = null;
                return;
            }

            // 月が一致するかチェック
            if (_currentSelectedCard.Data.month == clickedCard.Data.month)
            {
                Debug.Log($"【手札獲得一致】{_currentSelectedCard.Data.month}月が一致しました！");

                // ローカル変数に退避させてからクリア
                Card hand = _currentSelectedCard;
                Card field = clickedCard;

                _currentSelectedCard = null;

                // 獲得処理と役確認が終わった後に、山札めくりフェーズ（DrawFromDeckRoutine）を実行する
                CollectPair(hand, field, true, () => {
                    StartCoroutine(DrawFromDeckRoutine(true));
                });
            }
            else
            {
                Debug.LogWarning("月の違う札です。合わせられません。");
            }
        }
    }

    private IEnumerator DrawFromDeckRoutine(bool isPlayer)
    {
        _currentState = TurnState.CheckingMatch;

        yield return new WaitForSeconds(0.8f);

        if (_deck.Count == 0)
        {
            Debug.LogWarning("山札が空になりました。");
            SetNextTurn(isPlayer);
            yield break;
        }

        Card drawnCard = _deck[_deck.Count - 1];
        _deck.RemoveAt(_deck.Count - 1);

        drawnCard.transform.SetParent(fieldParent);
        drawnCard.SetFaceUp(true);
        Debug.Log($"山札からめくった札: {drawnCard.Data.month}月 ({drawnCard.Data.type})");

        Card matchedFieldCard = null;
        foreach (Transform fieldCardTr in fieldParent)
        {
            Card fieldCard = fieldCardTr.GetComponent<Card>();
            if (fieldCard != null && fieldCard != drawnCard)
            {
                if (drawnCard.Data.month == fieldCard.Data.month)
                {
                    matchedFieldCard = fieldCard;
                    break;
                }
            }
        }

        yield return new WaitForSeconds(1.0f);

        // ★制御フラグ：山札めくり後の処理（獲得演出やUI閉じ待ち）が完了したか
        bool isDrawingProcessDone = false;

        if (matchedFieldCard != null)
        {
            Debug.Log($"【山札めくり一致】{drawnCard.Data.month}月が場札と一致！獲得します。");

            // 獲得処理を呼び出し、役ウィンドウが閉じられたタイミングでフラグを true にする
            CollectPair(drawnCard, matchedFieldCard, isPlayer, () => {
                isDrawingProcessDone = true;
            });
        }
        else
        {
            Debug.Log($"【山札めくり不一致】一致する月がないため、場札に加えます。");
            isDrawingProcessDone = true; // 一致しなかった場合は即座に進行可能にする
        }

        // ラムダ式内（ウィンドウを閉じるボタンが押されるなど）でフラグが立てられるまでコルーチンを待機
        yield return new WaitUntil(() => isDrawingProcessDone);

        // 場札を綺麗に並び替える
        RearrangeFieldCards();

        yield return new WaitForSeconds(0.8f);

        // ➔ 山札から引き終わったため、ヒットの有無に関わらずここで確実に相手へのターン交代を行います
        SetNextTurn(isPlayer);
    }

    // ターンを交代する補助関数
    void SetNextTurn(bool currentIsPlayer)
    {
        if (currentIsPlayer)
        {
            // プレイヤーが引き終わったのでNPCのターンへ
            _currentState = TurnState.NPCTurn;
            StartCoroutine(NPCTurnRoutine());
        }
        else
        {
            // NPCが引き終わったのでプレイヤーのターンへ
            _currentState = TurnState.PlayerTurn;
            Debug.Log("あなたのターンです。");
        }
    }

    // ペアの獲得処理 (isPlayer: プレイヤーかNPCか, onComplete: 演出や確認が全て終了した時に呼ぶコールバック)
    void CollectPair(Card handCard, Card fieldCard, bool isPlayer, System.Action onComplete)
    {
        // 1. 獲得エリアへ移動
        MoveToCapturedArea(handCard, isPlayer);
        MoveToCapturedArea(fieldCard, isPlayer);

        // 2. 成立したすべての役をリストで取得
        List<YakuResult> activeYakus = CheckAllYaku(isPlayer);

        // 3. 役が1つ以上成立している場合の処理（現時点ではプレイヤー側のみUI表示）
        if (isPlayer && activeYakus.Count > 0 && yakuWindowManager != null)
        {
            // UI表示中は一時的に操作をロック
            _currentState = TurnState.CheckingMatch;

            // 複数の役名を「 ・ 」で結合
            string combinedName = string.Join(" ・ ", activeYakus.Select(y => y.Name));
            // 点数の合計
            int totalPoints = activeYakus.Sum(y => y.Points);

            // UIを表示し、プレイヤーがウィンドウを閉じたらコールバックを実行
            yakuWindowManager.ShowYaku(combinedName, totalPoints, () => {
                onComplete?.Invoke();
            });
        }
        else
        {
            // 役が成立していない、またはNPCの場合は、立ち止まらずに即次のステップへ
            onComplete?.Invoke();
        }
    }

    // 1枚のカードをタイプに応じた獲得エリアに移動させる
    void MoveToCapturedArea(Card card, bool isPlayer)
    {
        // 選択状態の見た目を完全に初期化
        card.SetSelected(false);
        card.SetFaceUp(true); // 獲得札は常に表向き

        // 飛ばし先の親トランスフォームを決定する
        Transform targetParent = null;

        // 文字列のブレを考慮して小文字で判定
        string cardType = card.Data.type.ToLower();

        if (isPlayer)
        {
            if (cardType == "hikari") targetParent = pHikariParent;
            else if (cardType == "tane") targetParent = pTaneParent;
            else if (cardType == "tan" || cardType == "tanzaku") targetParent = pTanParent;
            else targetParent = pKasuParent;
        }
        else
        {
            if (cardType == "hikari") targetParent = eHikariParent;
            else if (cardType == "tane") targetParent = eTaneParent;
            else if (cardType == "tan" || cardType == "tanzaku") targetParent = eTanParent;
            else targetParent = eKasuParent;
        }

        // 親を付け替える
        card.transform.SetParent(targetParent);

        // 獲得エリア内での整列（簡易的に並べる）
        int childCount = targetParent.childCount;
        card.transform.localPosition = new Vector3(childCount * 0.2f, 0, -0.01f * childCount);
        card.transform.localRotation = Quaternion.identity;
    }

    private IEnumerator NPCTurnRoutine()
    {
        Debug.Log("NPCが考えています...");
        yield return new WaitForSeconds(1.5f);

        Card npcChoice = null;
        Card fieldChoice = null;

        foreach (Transform npcCardTr in enemyHandParent)
        {
            Card npcCard = npcCardTr.GetComponent<Card>();
            foreach (Transform fieldCardTr in fieldParent)
            {
                Card fieldCard = fieldCardTr.GetComponent<Card>();
                if (npcCard.Data.month == fieldCard.Data.month)
                {
                    npcChoice = npcCard;
                    fieldChoice = fieldCard;
                    break;
                }
            }
            if (npcChoice != null) break;
        }

        if (npcChoice != null && fieldChoice != null)
        {
            Debug.Log($"【NPC獲得】{npcChoice.Data.month}月が一致しました。");

            // 獲得処理が全て完了した後のコールバックで山札めくりフェーズへ進む
            CollectPair(npcChoice, fieldChoice, false, () => {
                StartCoroutine(DrawFromDeckRoutine(false));
            });
        }
        else
        {
            if (enemyHandParent.childCount > 0)
            {
                Card discard = enemyHandParent.GetChild(0).GetComponent<Card>();
                Debug.Log($"NPCは一致する札がないため、{discard.Data.month}月を場に捨てました。");
                discard.transform.SetParent(fieldParent);
                discard.SetFaceUp(true);
                RearrangeFieldCards();
            }

            // 一致する札がなく場に捨てた場合も、同様に山札めくりフェーズへ進む
            StartCoroutine(DrawFromDeckRoutine(false));
        }
    }

    private List<YakuResult> CheckAllYaku(bool isPlayer)
    {
        // プレイヤーかNPCかに応じて、スキャン対象の親トランスフォーム（獲得エリア）を決定
        List<CardData> capturedCards = new List<CardData>();
        Transform[] targets = isPlayer
            ? new Transform[] { pHikariParent, pTaneParent, pTanParent, pKasuParent }
            : new Transform[] { eHikariParent, eTaneParent, eTanParent, eKasuParent };

        // 指定された獲得エリアから、純粋なデータ（CardData）だけを抽出してリストに溜める
        foreach (var parent in targets)
        {
            if (parent == null) continue;
            foreach (Transform child in parent)
            {
                Card card = child.GetComponent<Card>();
                if (card != null && card.Data != null)
                {
                    capturedCards.Add(card.Data);
                }
            }
        }

        // 役判定ロジックにデータリストを渡して、成立している役のリストを受け取る
        return YakuEvaluator.CheckAllYaku(capturedCards);
    }
}