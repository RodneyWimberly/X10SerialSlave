#include <x10.h>                       // X10 lib is used for transmitting X10
#include <x10constants.h>              // X10 Lib constants


#define RPT_SEND 2 

#define ZCROSS_PIN     2               // BLK pin 1 of PSC05
#define RCVE_PIN       4               // GRN pin 3 of PSC05
#define TRANS_PIN      3               // YEL pin 4 of PSC05
#define LED_PIN        13              // for testing 

x10 x10COntroller = x10(ZCROSS_PIN, TRANS_PIN, RCVE_PIN, LED_PIN);// set up a x10 library instance:

void setup() 
{
	Serial.begin(115200);
	delay(500);
	Serial.println("X10");
}

// A simple test program that demonstrates integrated send/receive
// prints X10 input, then sets D5 on/off if unit code on input was 1
void loop() 
{
	if (Serial.available() >= 3)
	{
		byte houseCode = toupper(Serial.read());
		byte unitCode = toupper(Serial.read());
		byte commandCode = toupper(Serial.read());
		x10COntroller.write(houseCode, unitCode, commandCode);
	}

	/*byte* data = new byte[3];
	if (Serial.available())
	{
		Serial.readBytes(data, 3);
		x10COntroller.write(data[0], data[1], data[2]);
	}*/
}
