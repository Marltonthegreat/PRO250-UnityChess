using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityChess;
using UnityChess.Engine;
using UnityEngine;

public class GameManager : MonoBehaviourSingleton<GameManager>
{
    public static event Action NewGameStartedEvent;
    public static event Action GameEndedEvent;
    public static event Action GameResetToHalfMoveEvent;
    public static event Action MoveExecutedEvent;

    public Board CurrentBoard
    {
        get
        {
            game.BoardTimeline.TryGetCurrent(out Board currentBoard);
            return currentBoard;
        }
    }

    public Side SideToMove
    {
        get
        {
            game.ConditionsTimeline.TryGetCurrent(out GameConditions currentConditions);
            return currentConditions.SideToMove;
        }
    }

    public Side StartingSide => game.ConditionsTimeline[0].SideToMove;
    public Timeline<HalfMove> HalfMoveTimeline => game.HalfMoveTimeline;
    public int LatestHalfMoveIndex => game.HalfMoveTimeline.HeadIndex;
    public int FullMoveNumber => StartingSide switch
    {
        Side.White => LatestHalfMoveIndex / 2 + 1,
        Side.Black => (LatestHalfMoveIndex + 1) / 2 + 1,
        _ => -1
    };

    private bool isWhiteAI;
    private bool isBlackAI;

    public List<(Square, Piece)> CurrentPieces
    {
        get
        {
            currentPiecesBacking.Clear();
            for (int file = 1; file <= 8; file++)
            {
                for (int rank = 1; rank <= 8; rank++)
                {
                    Piece piece = CurrentBoard[file, rank];
                    if (piece != null) currentPiecesBacking.Add((new Square(file, rank), piece));
                }
            }

            return currentPiecesBacking;
        }
    }


    private readonly List<(Square, Piece)> currentPiecesBacking = new List<(Square, Piece)>();

    [SerializeField] private UnityChessDebug unityChessDebug;
    private Game game;
    private FENSerializer fenSerializer;
    private PGNSerializer pgnSerializer;
    private CancellationTokenSource promotionUITaskCancellationTokenSource;
    private ElectedPiece userPromotionChoice = ElectedPiece.None;
    private Dictionary<GameSerializationType, IGameSerializer> serializersByType;
    private GameSerializationType selectedSerializationType = GameSerializationType.FEN;

    private IUCIEngine uciEngine;

    public void Start()
    {
        VisualPiece.VisualPieceMoved += OnPieceMoved;

        serializersByType = new Dictionary<GameSerializationType, IGameSerializer>
        {
            [GameSerializationType.FEN] = new FENSerializer(),
            [GameSerializationType.PGN] = new PGNSerializer()
        };

        //StartNewGame();
        StartNewGame(false, true, GeneratePairs(GenerateAllPositions()));

#if DEBUG_VIEW
		unityChessDebug.gameObject.SetActive(true);
		unityChessDebug.enabled = true;
#endif
    }

    private void OnDestroy()
    {
        uciEngine?.ShutDown();
    }

    private char[] GenerateAllPositions()
    {
        char[] positions = new char[8];

        //generate two bishops
        int bishopOnePos = UnityEngine.Random.Range(0, 4);
        int bishopTwoPos = UnityEngine.Random.Range(0, 4);

        positions[bishopOnePos * 2] = 'B';
        positions[1 + bishopTwoPos * 2] = 'B';

        //generate queen
        GeneratePosition(ref positions, 6, 'Q');

        //generate knightOne
        GeneratePosition(ref positions, 5, 'N');

        //generate knightTwo
        GeneratePosition(ref positions, 4, 'N');

        //generate remaining pieces
        GenerateKingRooksPositions(ref positions);

        return positions;
    }

    private void GeneratePosition(ref char[] positions, int maxExclusive, char symbol)
    {
        int piecePos = UnityEngine.Random.Range(0, maxExclusive);

        int[] emptyIndicies = CheckEmptyIndicies(positions);

        positions[emptyIndicies[piecePos]] = symbol;
    }

    private void GenerateKingRooksPositions(ref char[] positions)
    {
        int[] emptyIndicies = CheckEmptyIndicies(positions);

        positions[emptyIndicies[0]] = 'R';
        positions[emptyIndicies[1]] = 'K';
        positions[emptyIndicies[2]] = 'R';

    }

    private int[] CheckEmptyIndicies(char[] array)
    {
        List<int> emptyIndicies = new List<int>();

        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] == '\0') emptyIndicies.Add(i);
        }

        return emptyIndicies.ToArray();
    }

    private (Square, Piece)[] GeneratePairs(char[] positions)
    {
        (Square, Piece)[] startingPositionPieces = {
            (new Square("a1"), DeterminePiece(positions[0], Side.White)),
            (new Square("b1"), DeterminePiece(positions[1], Side.White)),
            (new Square("c1"), DeterminePiece(positions[2], Side.White)),
            (new Square("d1"), DeterminePiece(positions[3], Side.White)),
            (new Square("e1"), DeterminePiece(positions[4], Side.White)),
            (new Square("f1"), DeterminePiece(positions[5], Side.White)),
            (new Square("g1"), DeterminePiece(positions[6], Side.White)),
            (new Square("h1"), DeterminePiece(positions[7], Side.White)),

            (new Square("a2"), new Pawn(Side.White)),
            (new Square("b2"), new Pawn(Side.White)),
            (new Square("c2"), new Pawn(Side.White)),
            (new Square("d2"), new Pawn(Side.White)),
            (new Square("e2"), new Pawn(Side.White)),
            (new Square("f2"), new Pawn(Side.White)),
            (new Square("g2"), new Pawn(Side.White)),
            (new Square("h2"), new Pawn(Side.White)),

            (new Square("a7"), new Pawn(Side.Black)),
            (new Square("b7"), new Pawn(Side.Black)),
            (new Square("c7"), new Pawn(Side.Black)),
            (new Square("d7"), new Pawn(Side.Black)),
            (new Square("e7"), new Pawn(Side.Black)),
            (new Square("f7"), new Pawn(Side.Black)),
            (new Square("g7"), new Pawn(Side.Black)),
            (new Square("h7"), new Pawn(Side.Black)),

            (new Square("a8"), DeterminePiece(positions[0], Side.Black)),
            (new Square("b8"), DeterminePiece(positions[1], Side.Black)),
            (new Square("c8"), DeterminePiece(positions[2], Side.Black)),
            (new Square("d8"), DeterminePiece(positions[3], Side.Black)),
            (new Square("e8"), DeterminePiece(positions[4], Side.Black)),
            (new Square("f8"), DeterminePiece(positions[5], Side.Black)),
            (new Square("g8"), DeterminePiece(positions[6], Side.Black)),
            (new Square("h8"), DeterminePiece(positions[7], Side.Black))
        };

        return startingPositionPieces;
    }

    private Piece DeterminePiece(char piece, Side side)
    {
        switch (piece)
        {
            case 'K':
                return new King(side);
            case 'Q':
                return new Queen(side);
            case 'R':
                return new Rook(side);
            case 'N':
                return new Knight(side);
            case 'B':
                return new Bishop(side);
            default:
                throw new ArgumentOutOfRangeException();
        }


    }

    public async void StartNewGame(bool isWhiteAI = false, bool isBlackAI = true, params (Square, Piece)[] squarePiecePairs)
    {
        if (squarePiecePairs.Length == 0)
        {
            game = new Game();
        }
        else
        {
            game = new Game(GameConditions.NormalStartingConditions, squarePiecePairs);
        }

        this.isWhiteAI = isWhiteAI;
        this.isBlackAI = isBlackAI;

        if (isWhiteAI || isBlackAI)
        {
            if (uciEngine == null)
            {
                uciEngine = new MockUCIEngine();
                uciEngine.Start();
            }

            await uciEngine.SetupNewGame(game);
            NewGameStartedEvent?.Invoke();

            if (isWhiteAI)
            {
                Movement bestMove = await uciEngine.GetBestMove(10_000);
                DoAIMove(bestMove);
            }
        }
        else
        {
            NewGameStartedEvent?.Invoke();
        }
    }

    public string SerializeGame()
    {
        return serializersByType.TryGetValue(selectedSerializationType, out IGameSerializer serializer)
            ? serializer?.Serialize(game)
            : null;
    }

    public void LoadGame(string serializedGame)
    {
        game = serializersByType[selectedSerializationType].Deserialize(serializedGame);
        NewGameStartedEvent?.Invoke();
    }

    public void ResetGameToHalfMoveIndex(int halfMoveIndex)
    {
        if (!game.ResetGameToHalfMoveIndex(halfMoveIndex)) return;

        UIManager.Instance.SetActivePromotionUI(false);
        promotionUITaskCancellationTokenSource?.Cancel();
        GameResetToHalfMoveEvent?.Invoke();
    }

    private bool TryExecuteMove(Movement move)
    {
        if (!game.TryExecuteMove(move))
        {
            return false;
        }

        HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate)
        {
            BoardManager.Instance.SetActiveAllPieces(false);
            GameEndedEvent?.Invoke();
        }
        else
        {
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(SideToMove);
        }

        MoveExecutedEvent?.Invoke();

        return true;
    }

    private async Task<bool> TryHandleSpecialMoveBehaviourAsync(SpecialMove specialMove)
    {
        switch (specialMove)
        {
            case CastlingMove castlingMove:
                BoardManager.Instance.CastleRook(castlingMove.RookSquare, castlingMove.GetRookEndSquare());
                return true;
            case EnPassantMove enPassantMove:
                BoardManager.Instance.TryDestroyVisualPiece(enPassantMove.CapturedPawnSquare);
                return true;
            case PromotionMove { PromotionPiece: null } promotionMove:
                UIManager.Instance.SetActivePromotionUI(true);
                BoardManager.Instance.SetActiveAllPieces(false);

                promotionUITaskCancellationTokenSource?.Cancel();
                promotionUITaskCancellationTokenSource = new CancellationTokenSource();

                ElectedPiece choice = await Task.Run(GetUserPromotionPieceChoice, promotionUITaskCancellationTokenSource.Token);

                UIManager.Instance.SetActivePromotionUI(false);
                BoardManager.Instance.SetActiveAllPieces(true);

                if (promotionUITaskCancellationTokenSource == null
                    || promotionUITaskCancellationTokenSource.Token.IsCancellationRequested
                ) { return false; }

                promotionMove.SetPromotionPiece(
                    PromotionUtil.GeneratePromotionPiece(choice, SideToMove)
                );
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
                BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);

                promotionUITaskCancellationTokenSource = null;
                return true;
            case PromotionMove promotionMove:
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
                BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);

                return true;
            default:
                return false;
        }
    }

    private ElectedPiece GetUserPromotionPieceChoice()
    {
        while (userPromotionChoice == ElectedPiece.None) { }

        ElectedPiece result = userPromotionChoice;
        userPromotionChoice = ElectedPiece.None;
        return result;
    }

    public void ElectPiece(ElectedPiece choice)
    {
        userPromotionChoice = choice;
    }

    private async void OnPieceMoved(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null)
    {
        Square endSquare = new Square(closestBoardSquareTransform.name);

        if (!game.TryGetLegalMove(movedPieceInitialSquare, endSquare, out Movement move))
        {
            movedPieceTransform.position = movedPieceTransform.parent.position;
#if DEBUG_VIEW
			Piece movedPiece = CurrentBoard[movedPieceInitialSquare];
			game.TryGetLegalMovesForPiece(movedPiece, out ICollection<Movement> legalMoves);
			UnityChessDebug.ShowLegalMovesInLog(legalMoves);
#endif
            return;
        }

        if (move is PromotionMove promotionMove)
        {
            promotionMove.SetPromotionPiece(promotionPiece);
        }

        if ((move is not SpecialMove specialMove || await TryHandleSpecialMoveBehaviourAsync(specialMove))
            && TryExecuteMove(move)
        )
        {
            if (move is not SpecialMove) { BoardManager.Instance.TryDestroyVisualPiece(move.End); }

            if (move is PromotionMove)
            {
                movedPieceTransform = BoardManager.Instance.GetPieceGOAtPosition(move.End).transform;
            }

            movedPieceTransform.parent = closestBoardSquareTransform;
            movedPieceTransform.position = closestBoardSquareTransform.position;
        }

        bool gameIsOver = game.HalfMoveTimeline.TryGetCurrent(out HalfMove lastHalfMove)
                          && lastHalfMove.CausedStalemate || lastHalfMove.CausedCheckmate;
        if (!gameIsOver
            && (SideToMove == Side.White && isWhiteAI
                || SideToMove == Side.Black && isBlackAI)
        )
        {
            Movement bestMove = await uciEngine.GetBestMove(10_000);
            DoAIMove(bestMove);
        }
    }

    private void DoAIMove(Movement move)
    {
        GameObject movedPiece = BoardManager.Instance.GetPieceGOAtPosition(move.Start);
        GameObject endSquareGO = BoardManager.Instance.GetSquareGOByPosition(move.End);
        OnPieceMoved(
            move.Start,
            movedPiece.transform,
            endSquareGO.transform,
            (move as PromotionMove)?.PromotionPiece
        );
    }

    public bool HasLegalMoves(Piece piece)
    {
        return game.TryGetLegalMovesForPiece(piece, out _);
    }
}