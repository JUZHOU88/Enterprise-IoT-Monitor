#include <Arduino.h>
#include <WiFi.h>
#include <HTTPClient.h>
#include <OneWire.h>
#include <DallasTemperature.h>
#include "MonitorLogic.h"
#include "secrets.h" // Copy include/secrets.example.h to include/secrets.h.

// Low-voltage demo only; check your board pinout.
constexpr int SENSOR_PIN = 4;
constexpr int DOOR_PIN = 27;
constexpr int LED_PIN = 2;
constexpr uint32_t PERIOD_MS = 5000;

OneWire oneWire(SENSOR_PIN);
DallasTemperature ds(&oneWire);
monitor::MonitorLogic logic;
uint32_t nextSampleAt = 0;
uint32_t conversionStartedAt = 0;
uint32_t sequenceNumber = 0;
uint32_t bootId = 0;
bool pending = false;

void setup()
{
    Serial.begin(115200);
    pinMode(DOOR_PIN, INPUT_PULLUP);
    pinMode(LED_PIN, OUTPUT);
    digitalWrite(LED_PIN, LOW);
    ds.begin();
    ds.setResolution(10);
    ds.setWaitForConversion(false);
    WiFi.mode(WIFI_STA);
    WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
    bootId = esp_random();
}

void loop()
{
    uint32_t now = millis();
    bool open = digitalRead(DOOR_PIN) == LOW;
    logic.updateDoor(open, now);

    if (!pending && static_cast<int32_t>(now - nextSampleAt) >= 0)
    {
        ds.requestTemperatures();
        conversionStartedAt = now;
        pending = true;
        nextSampleAt = now + PERIOD_MS;
    }
    if (!pending || static_cast<uint32_t>(now - conversionStartedAt) < 250) return;
    pending = false;

    float temperature = ds.getTempCByIndex(0);
    bool valid = temperature != DEVICE_DISCONNECTED_C &&
        temperature >= -55 && temperature <= 125 && isfinite(temperature);
    auto decision = logic.evaluate(temperature, valid, now);
    digitalWrite(LED_PIN, decision.alert ? HIGH : LOW);

    if (WiFi.status() != WL_CONNECTED)
    {
        static uint32_t lastReconnect = 0;
        if (static_cast<uint32_t>(now - lastReconnect) > 10000)
        {
            lastReconnect = now;
            WiFi.reconnect();
        }
        Serial.println("Offline; telemetry not uploaded");
        return;
    }

    char eventId[32];
    snprintf(eventId, sizeof(eventId), "%08lx-%lu",
        (unsigned long)bootId, (unsigned long)++sequenceNumber);
    char payload[256];
    if (valid)
    {
        snprintf(payload, sizeof(payload),
            "{\"deviceId\":\"%s\",\"eventId\":\"%s\",\"temperatureC\":%.2f,\"doorOpen\":%s,\"sensorOk\":true,\"uptimeMs\":%lu}",
            MONITOR_DEVICE_ID, eventId, temperature, open ? "true" : "false", (unsigned long)now);
    }
    else
    {
        snprintf(payload, sizeof(payload),
            "{\"deviceId\":\"%s\",\"eventId\":\"%s\",\"temperatureC\":null,\"doorOpen\":%s,\"sensorOk\":false,\"uptimeMs\":%lu}",
            MONITOR_DEVICE_ID, eventId, open ? "true" : "false", (unsigned long)now);
    }

    WiFiClient client;
    HTTPClient http;
    http.setTimeout(2500); // Synchronous demo transport; future version should queue/retry.
    if (!http.begin(client, MONITOR_API_URL)) return;
    http.addHeader("Content-Type", "application/json");
    http.addHeader("X-Device-Key", MONITOR_API_KEY);
    int response = http.POST((uint8_t*)payload, strlen(payload));
    Serial.printf("Telemetry HTTP status: %d\n", response);
    http.end();
}
