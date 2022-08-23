namespace RangeVote2.Data
{
    public class Ballot
    {
        public Guid Id { get; set; }
        public Candidate[] Candidates { get; set; }
    }

    public class Candidate
    {
        public String Name { get; set; }
        public Int32 Score { get; set; }
        public String ElectionID { get; set; }
        public String Description { get; set; }
        public String ScoreString { get { return (Score / 10.0).ToString("N1"); } }
    }

    public class DBCandidate
    {
        public String Guid { get; set; }
        public String Name { get; set; }
        public Int32 Score { get; set; }
        public String ElectionID { get; set; }
        public String Description { get; set; }
    }
}
