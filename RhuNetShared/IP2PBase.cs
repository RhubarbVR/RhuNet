using MessagePack;
using System;

namespace RhuNetShared
{
    public interface IP2PBase
    { 
        [Key(0)]
        long ID { get; set; }        
    }
}
