namespace UniversalScanner
{
    public class NetworkAdapterInfo
    {
        public string InterfaceAlias { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public int InterfaceIndex { get; set; }
        public string MacAddress { get; set; }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(InterfaceAlias))
            {
                string text = InterfaceAlias + " (" + Status + ")";

                if (!string.IsNullOrWhiteSpace(Description))
                    text += " - " + Description;

                if (InterfaceIndex > 0)
                    text += " - Index " + InterfaceIndex;

                if (!string.IsNullOrWhiteSpace(MacAddress))
                    text += " - " + MacAddress;

                return text;
            }

            return base.ToString();
        }
    }
}
