using System;
using System.Collections.Generic;

namespace GameDB.Domain.Entities;

public partial class Developer
{
    public int DeveloperId { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<Game> Games { get; set; } = new List<Game>();
}
