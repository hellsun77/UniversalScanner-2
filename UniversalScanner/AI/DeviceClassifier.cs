using System;

namespace UniversalScanner.AI
{
    public sealed class DeviceClassifier
    {
        private readonly ConfidenceEngine confidenceEngine;

        public DeviceClassifier()
            : this(new ConfidenceEngine())
        {
        }

        public DeviceClassifier(ConfidenceEngine confidenceEngine)
        {
            this.confidenceEngine = confidenceEngine ?? throw new ArgumentNullException(nameof(confidenceEngine));
        }

        public DeviceClassification Classify(DeviceFingerprint fingerprint, KnowledgeBase knowledgeBase)
        {
            if (fingerprint == null)
                throw new ArgumentNullException(nameof(fingerprint));

            LearnedFingerprintMatch learnedMatch = knowledgeBase == null ? null : knowledgeBase.FindBestFingerprintMatch(fingerprint);
            LearnedBrandSuggestion brandSuggestion = null;
            if (knowledgeBase != null && KnowledgeBase.IsGenericVendor(fingerprint.Vendor))
                brandSuggestion = knowledgeBase.SuggestBrand(fingerprint);

            double confidence = confidenceEngine.Score(fingerprint, knowledgeBase, learnedMatch);
            string vendor = string.IsNullOrWhiteSpace(fingerprint.Vendor) ? "Unknown" : fingerprint.Vendor.Trim();
            string model = string.IsNullOrWhiteSpace(fingerprint.Model) ? "Unknown" : fingerprint.Model.Trim();

            return new DeviceClassification(vendor, model, confidence, knowledgeBase != null && knowledgeBase.IsLoaded, learnedMatch, brandSuggestion);
        }
    }

    public sealed class DeviceClassification
    {
        public DeviceClassification(string vendor, string model, double confidence, bool usedKnowledgeBase)
            : this(vendor, model, confidence, usedKnowledgeBase, null)
        {
        }

        public DeviceClassification(string vendor, string model, double confidence, bool usedKnowledgeBase, LearnedFingerprintMatch learnedMatch)
            : this(vendor, model, confidence, usedKnowledgeBase, learnedMatch, null)
        {
        }

        public DeviceClassification(string vendor, string model, double confidence, bool usedKnowledgeBase, LearnedFingerprintMatch learnedMatch, LearnedBrandSuggestion brandSuggestion)
        {
            Vendor = vendor;
            Model = model;
            Confidence = confidence;
            UsedKnowledgeBase = usedKnowledgeBase;
            LearnedMatch = learnedMatch;
            BrandSuggestion = brandSuggestion;
        }

        public string Vendor { get; private set; }
        public string Model { get; private set; }
        public double Confidence { get; private set; }
        public bool UsedKnowledgeBase { get; private set; }
        public LearnedFingerprintMatch LearnedMatch { get; private set; }
        public LearnedBrandSuggestion BrandSuggestion { get; private set; }
    }
}
