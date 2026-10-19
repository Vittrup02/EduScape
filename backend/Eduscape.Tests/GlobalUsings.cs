// The domain's Lock clashes with System.Threading.Lock from implicit usings; in this project Lock means the room object.
global using Lock = Eduscape.Domain.Rooms.Lock;
