using System;
using System.Collections.Generic;

namespace Com.MagicBeans
{
    /// <summary>
    /// 各命令的请求 PAYLOAD 构造（不含 seq；MagicBeansComm 会自动在前面拼 u32 seq）
    /// 与响应 PAYLOAD 解析。所有金额单位：分（cents）。多字节整数均为大端。
    /// </summary>
    public static class MbMessages
    {
        // ===== 请求（仅含 seq 的命令，PAYLOAD 为空，无需构造额外字段）=====
        // HELLO / STATUS / SPIN / BALANCE / LAST_RESULT：调用 SendRequest(cmd, null, ...) 即可

        // LINE_SET (REQ): u8 active_lines
        public static byte[] LineSetReq(byte activeLines)
        {
            var b = new List<byte>(); MbEndian.PutU8(b, activeLines); return b.ToArray();
        }
        // BET_SET (REQ): u8 per_line_bet
        public static byte[] BetSetReq(byte perLineBet)
        {
            var b = new List<byte>(); MbEndian.PutU8(b, perLineBet); return b.ToArray();
        }
        // BONUS (REQ): u8 door（0=A,1=B,2=C）
        public static byte[] BonusReq(byte door)
        {
            var b = new List<byte>(); MbEndian.PutU8(b, door); return b.ToArray();
        }
        // FREE_SPIN (REQ): u8 free_index, u8 remaining_before
        public static byte[] FreeSpinReq(byte freeIndex, byte remainingBefore)
        {
            var b = new List<byte>(); MbEndian.PutU8(b, freeIndex); MbEndian.PutU8(b, remainingBefore); return b.ToArray();
        }
        // DOUBLE (REQ): u8 guess（0=红太阳,1=蓝月亮）, u8 attempt（1~5）
        public static byte[] DoubleReq(byte guess, byte attempt)
        {
            var b = new List<byte>(); MbEndian.PutU8(b, guess); MbEndian.PutU8(b, attempt); return b.ToArray();
        }
        // LAST_RESULT (REQ): u32 target_seq
        public static byte[] LastResultReq(uint targetSeq)
        {
            var b = new List<byte>(); MbEndian.PutU32(b, targetSeq); return b.ToArray();
        }

        // ===== 响应解析（PAYLOAD 首 4 字节为 seq）=====
        public class HelloResp
        {
            public uint seq; public byte major; public byte minor; public byte featureFlags;
            public static HelloResp Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new HelloResp();
                o.seq = r.U32(); o.major = r.U8(); o.minor = r.U8(); o.featureFlags = r.U8(); return o;
            }
        }

        public class StatusResp
        {
            public uint seq; public GameState gameState; public uint currentSeq;
            public byte currentBet; public uint balance; public byte freeRemaining;
            public byte freeTotal; public byte freePlayed; public byte doubleAttempt;
            public uint doubleAmount; public byte jpOnline; public uint uptimeSec;
            public byte currentLines;
            public static StatusResp Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new StatusResp();
                o.seq = r.U32(); o.gameState = (GameState)r.U8(); o.currentSeq = r.U32();
                o.currentBet = r.U8(); o.balance = r.U32(); o.freeRemaining = r.U8();
                o.freeTotal = r.U8(); o.freePlayed = r.U8(); o.doubleAttempt = r.U8();
                o.doubleAmount = r.U32(); o.jpOnline = r.U8(); o.uptimeSec = r.U32();
                o.currentLines = r.U8(); return o;
            }
        }

        public class LineWin { public byte lineId; public byte symbolId; public byte count; public uint winAmount; }

        public class SpinResp
        {
            public uint seq; public byte perLineBet; public uint totalBet;
            public byte[] board = new byte[15]; // col-major: col0(r0,r1,r2), col1(r0,r1,r2) ... col4
            public byte scatterCount; public uint scatterCash;
            public byte freeGameAwarded; public byte beanTriggerCount; public byte beanTriggerLineId;
            public BeanTriggerType beanTriggerType; public uint jpPayout;
            public uint balanceAfter; public uint baseWinTotal; public uint spinWinTotal;
            public byte baseWinCount; public byte activeLines;
            public LineWin[] lineWins;
            public static SpinResp Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new SpinResp();
                o.seq = r.U32(); o.perLineBet = r.U8(); o.totalBet = r.U32();
                for (int i = 0; i < 15; i++) o.board[i] = r.U8();
                o.scatterCount = r.U8(); o.scatterCash = r.U32();
                o.freeGameAwarded = r.U8(); o.beanTriggerCount = r.U8(); o.beanTriggerLineId = r.U8();
                o.beanTriggerType = (BeanTriggerType)r.U8(); o.jpPayout = r.U32();
                o.balanceAfter = r.U32(); o.baseWinTotal = r.U32(); o.spinWinTotal = r.U32();
                o.baseWinCount = r.U8(); o.activeLines = r.U8();
                o.lineWins = new LineWin[o.baseWinCount];
                for (int i = 0; i < o.baseWinCount; i++)
                {
                    var lw = new LineWin(); lw.lineId = r.U8(); lw.symbolId = r.U8(); lw.count = r.U8(); lw.winAmount = r.U32();
                    o.lineWins[i] = lw;
                }
                return o;
            }
        }

        public class BonusResp
        {
            public uint seq; public BonusGame bonusGameId; public bool jp3Hit; public uint jp3Payout;
            public bool bonusPlayed; public bool maxWinCapped; public uint bonusWin; public uint balanceAfter;
            public byte revealCount; public uint[] revealWins;
            public static BonusResp Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new BonusResp();
                o.seq = r.U32(); o.bonusGameId = (BonusGame)r.U8(); o.jp3Hit = r.U8() == 1;
                o.jp3Payout = r.U32(); o.bonusPlayed = r.U8() == 1; o.maxWinCapped = r.U8() == 1;
                o.bonusWin = r.U32(); o.balanceAfter = r.U32(); o.revealCount = r.U8();
                o.revealWins = new uint[o.revealCount];
                for (int i = 0; i < o.revealCount; i++) o.revealWins[i] = r.U32();
                return o;
            }
        }

        public class FreeSpinResp
        {
            public uint seq; public byte freeIndex; public byte[] board = new byte[15];
            public uint freeWinThisRound; public byte remainingAfter; public byte freeGameEnded;
            public uint freeTotalWin; public uint balanceAfter; public byte baseWinCount; public byte activeLines;
            public LineWin[] lineWins;
            public static FreeSpinResp Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new FreeSpinResp();
                o.seq = r.U32(); o.freeIndex = r.U8();
                for (int i = 0; i < 15; i++) o.board[i] = r.U8();
                o.freeWinThisRound = r.U32(); o.remainingAfter = r.U8(); o.freeGameEnded = r.U8();
                o.freeTotalWin = r.U32(); o.balanceAfter = r.U32(); o.baseWinCount = r.U8(); o.activeLines = r.U8();
                o.lineWins = new LineWin[o.baseWinCount];
                for (int i = 0; i < o.baseWinCount; i++)
                {
                    var lw = new LineWin(); lw.lineId = r.U8(); lw.symbolId = r.U8(); lw.count = r.U8(); lw.winAmount = r.U32();
                    o.lineWins[i] = lw;
                }
                return o;
            }
        }

        public class DoubleResp
        {
            public uint seq; public byte actualFace; public bool result; public uint currentAmount;
            public bool doubleEnded; public uint balanceAfter;
            public static DoubleResp Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new DoubleResp();
                o.seq = r.U32(); o.actualFace = r.U8(); o.result = r.U8() == 1; o.currentAmount = r.U32();
                o.doubleEnded = r.U8() == 1; o.balanceAfter = r.U32(); return o;
            }
        }

        public class BalanceResp
        {
            public uint seq; public uint balance; public uint lastSeq;
            public static BalanceResp Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new BalanceResp();
                o.seq = r.U32(); o.balance = r.U32(); o.lastSeq = r.U32(); return o;
            }
        }

        public class LastResultResp
        {
            public uint seq; public uint targetSeq; public Cmd lastCmd; public byte resultState;
            public uint balanceAfter; public uint winAmount;
            public static LastResultResp Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new LastResultResp();
                o.seq = r.U32(); o.targetSeq = r.U32(); o.lastCmd = (Cmd)r.U8(); o.resultState = r.U8();
                o.balanceAfter = r.U32(); o.winAmount = r.U32(); return o;
            }
        }

        // ===== PUSH 帧（PAYLOAD 首字段即业务字段，无 seq）=====
        public class HeartbeatPush
        {
            public uint uptimeSec; public GameState gameState;
            public static HeartbeatPush Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new HeartbeatPush();
                o.uptimeSec = r.U32(); o.gameState = (GameState)r.U8(); return o;
            }
        }

        public class JpPoolPush
        {
            public uint jp1Display; public uint jp2Display; public uint jp3Display;
            public bool jp1Eligible; public bool jp2Eligible; public bool jp3Eligible;
            public static JpPoolPush Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new JpPoolPush();
                o.jp1Display = r.U32(); o.jp2Display = r.U32(); o.jp3Display = r.U32();
                o.jp1Eligible = r.U8() == 1; o.jp2Eligible = r.U8() == 1; o.jp3Eligible = r.U8() == 1; return o;
            }
        }

        public class KeyPush
        {
            public byte keyId;
            public static KeyPush Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new KeyPush(); o.keyId = r.U8(); return o;
            }
        }

        public class ErrorPush
        {
            public byte errorCode; public byte severity; public uint contextSeq;
            public static ErrorPush Parse(byte[] p)
            {
                var r = new ByteReader(p); var o = new ErrorPush();
                o.errorCode = r.U8(); o.severity = r.U8(); o.contextSeq = r.U32(); return o;
            }
        }
    }
}
