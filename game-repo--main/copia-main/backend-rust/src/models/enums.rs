use serde::{Deserialize, Serialize};

pub type MatchId = i64;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum MatchStatus {
    WaitingCreateTx,
    CreatedOnChain,
    JoinedOnChain,
    InProgress,
    ResultPendingFinalize,
    Finalizing,
    Settled,
    Refunded,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ChainJobType {
    Settle,
    ForceRefund,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ChainJobStatus {
    Pending,
    Submitted,
    Retrying,
    Confirmed,
    Failed,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ResultOutcome {
    Winner,
    Broken,
}

