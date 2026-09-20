namespace UniversalScanner.AI
{
    public sealed class ConfidenceEngine
    {
        public double Score(DeviceFingerprint fingerprint, KnowledgeBase knowledgeBase)
        {
            LearnedFingerprintMatch learnedMatch = knowledgeBase == null ? null : knowledgeBase.FindBestFingerprintMatch(fingerprint);
            return Score(fingerprint, knowledgeBase, learnedMatch);
        }

        public double Score(DeviceFingerprint fingerprint, KnowledgeBase knowledgeBase, LearnedFingerprintMatch learnedMatch)
        {
            if (fingerprint == null)
                return 0.0;

            double score = 0.0;

            if (!string.IsNullOrWhiteSpace(fingerprint.Vendor))
                score += 0.35;
            if (!string.IsNullOrWhiteSpace(fingerprint.Model))
                score += 0.25;
            if (!string.IsNullOrWhiteSpace(fingerprint.Protocol))
                score += 0.15;
            if (!string.IsNullOrWhiteSpace(fingerprint.MacAddress))
                score += 0.10;
            if (!string.IsNullOrWhiteSpace(fingerprint.OnvifProfile))
                score += 0.10;
            if (knowledgeBase != null && knowledgeBase.IsLoaded)
                score += 0.05;
            if (learnedMatch != null)
                score += learnedMatch.MatchScore * 0.20;

            return score > 1.0 ? 1.0 : score;
        }
    }
}
