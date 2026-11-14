#include "MonitorLogic.h"
#include <cassert>
#include <iostream>
#include <limits>
using monitor::MonitorLogic;
int main() {
    MonitorLogic m;
    m.updateDoor(false,0);
    assert(!m.evaluate(23,true,0).alert);
    assert(m.evaluate(30,true,1).temperatureAlarm);
    assert(m.evaluate(0,false,2).sensorFault);
    m.updateDoor(true,5);
    assert(!m.evaluate(23,true,2999).doorAlarm);
    assert(m.evaluate(23,true,3005).doorAlarm);
    m.updateDoor(false,3010);
    assert(!m.evaluate(23,true,3010).doorAlarm);
    assert(m.evaluate(std::numeric_limits<float>::quiet_NaN(),true,10).sensorFault);
    std::cout<<"Portable C++ embedded logic checks passed\n";
}
