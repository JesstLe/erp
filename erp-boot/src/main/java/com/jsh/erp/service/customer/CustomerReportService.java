package com.jsh.erp.service.customer;

import com.jsh.erp.datasource.mappers.CustomerReportMapperEx;
import org.springframework.stereotype.Service;

import javax.annotation.Resource;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.time.format.DateTimeParseException;
import java.util.Date;
import java.util.List;
import java.util.Map;

@Service
public class CustomerReportService {
    @Resource
    private CustomerReportMapperEx customerReportMapperEx;

    public List<Map<String, Object>> listConsumption(String start, String end, Long depotId, String memberKey) {
        Date s = parseDateTime(start);
        Date e = parseDateTime(end);
        return customerReportMapperEx.listConsumption(s, e, depotId, safeTrim(memberKey));
    }

    public List<Map<String, Object>> listCreditBill(String start, String end, Long depotId, String memberKey, String status) {
        Date s = parseDateTime(start);
        Date e = parseDateTime(end);
        return customerReportMapperEx.listCreditBill(s, e, depotId, safeTrim(memberKey), safeTrim(status));
    }

    private static String safeTrim(String s) {
        if (s == null) return null;
        String v = s.trim();
        return v.isEmpty() ? null : v;
    }

    private static Date parseDateTime(String s) {
        if (s == null) return null;
        String v = s.trim();
        if (v.isEmpty()) return null;
        try {
            if (v.length() == 10) {
                LocalDate d = LocalDate.parse(v, DateTimeFormatter.ofPattern("yyyy-MM-dd"));
                return Date.from(d.atStartOfDay().atZone(ZoneId.systemDefault()).toInstant());
            }
            if (v.length() == 19) {
                LocalDateTime dt = LocalDateTime.parse(v, DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss"));
                return Date.from(dt.atZone(ZoneId.systemDefault()).toInstant());
            }
            LocalDateTime dt = LocalDateTime.parse(v, DateTimeFormatter.ISO_LOCAL_DATE_TIME);
            return Date.from(dt.atZone(ZoneId.systemDefault()).toInstant());
        } catch (DateTimeParseException ex) {
            return null;
        }
    }
}

