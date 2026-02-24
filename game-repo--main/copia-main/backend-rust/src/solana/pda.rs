//! PDA derivation placeholder.
//!
//! For your program:
//! - game PDA = seeds ["game", player1, authority, match_id_le]
//! - vault PDA = seeds ["vault", game_pda]

#[allow(dead_code)]
pub struct MatchPdas {
    pub game_pda: String,
    pub vault_pda: String,
}

