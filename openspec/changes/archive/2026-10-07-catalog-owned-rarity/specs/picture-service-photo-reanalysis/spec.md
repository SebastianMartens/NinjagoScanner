## MODIFIED Requirements

### Requirement: ReanalyzePhoto preserves review status and pins a verified match
`ReanalyzePhoto` SHALL leave the photo's `ReviewStatus` exactly as it is at the moment the result is written, never resetting or setting it. When the photo's `ReviewStatus` is `verified` and its sidecar has both a series name and a card number, the analysis SHALL still re-run, but the resulting sidecar SHALL keep that series name and card number instead of a newly matched pair, consistent with how `Scan` treats verified photos. Every other analysis-derived field (card name, language, and the series name and card number of a photo that is not verified) SHALL be replaced by the new result, including values a person had previously edited by hand.

#### Scenario: Review status survives re-analysis
- **WHEN** `ReanalyzePhoto` completes for a photo whose `ReviewStatus` is `incorrect`
- **THEN** the photo's `ReviewStatus` is still `incorrect`

#### Scenario: Verified series and card number survive re-analysis
- **WHEN** `ReanalyzePhoto` completes for a `verified` photo with series name "Serie 3" and card number "12", and the new analysis would otherwise have matched a different series and card number
- **THEN** the sidecar's series name is still "Serie 3" and its card number is still "12"

#### Scenario: A photo that is not verified takes the new match
- **WHEN** `ReanalyzePhoto` completes for an `unreviewed` photo and the new analysis matches a different series and card number than before
- **THEN** the sidecar holds the newly matched series name and card number

#### Scenario: A review status change made during the analysis is not lost
- **WHEN** a photo's `ReviewStatus` is changed while its `ReanalyzePhoto` call is still running
- **THEN** the sidecar written when the analysis finishes carries the changed `ReviewStatus`, not the value it had when the call began

### Requirement: ReanalyzePhoto returns the updated card and is immediately visible
On success, `ReanalyzePhoto` SHALL return the photo's card entry as it stands after the sidecar was written. Any subsequent read of that photo's sidecar data - `ListCards`, `GetCardDetails` - SHALL reflect the new analysis result without waiting for any cache to expire.

#### Scenario: Response reflects the new result
- **WHEN** `ReanalyzePhoto` succeeds
- **THEN** the returned card entry carries the photo's newly written analysis status, card name, card number, series name, and language

#### Scenario: Subsequent reads see the new result
- **WHEN** `ListCards` or `GetCardDetails` is called for the photo after `ReanalyzePhoto` succeeded
- **THEN** the data returned reflects the new analysis result
