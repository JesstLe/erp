package com.jsh.erp.service.customer;

import com.jsh.erp.datasource.entities.Supplier;
import com.jsh.erp.datasource.mappers.CustomerDashboardMapperEx;
import org.springframework.stereotype.Service;

import javax.annotation.Resource;
import java.math.BigDecimal;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.time.format.DateTimeParseException;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@Service
public class CustomerDashboardService {
    @Resource
    private CustomerDashboardMapperEx customerDashboardMapperEx;

    public Map<String, Object> summary(String range, String start, String end, Long depotId) {
        DateRange dr = resolveRange(range, start, end);
        BigDecimal serviceAmount = customerDashboardMapperEx.sumServiceAmount(dr.startTime, dr.endTime, depotId);
        BigDecimal productAmount = customerDashboardMapperEx.sumProductAmount(dr.startTime, dr.endTime, depotId);
        BigDecimal incomeTotal = safeAdd(serviceAmount, productAmount);

        Long consumerCount = customerDashboardMapperEx.countConsumerDistinct(dr.startTime, dr.endTime, depotId);
        Long memberTotal = customerDashboardMapperEx.countMemberTotal();
        BigDecimal storedBalance = customerDashboardMapperEx.sumMemberAdvanceIn();

        Map<String, Object> map = new HashMap<>();
        map.put("incomeTotal", incomeTotal);
        map.put("consumerCount", consumerCount == null ? 0L : consumerCount);
        map.put("memberTotal", memberTotal == null ? 0L : memberTotal);
        map.put("storedBalance", storedBalance == null ? BigDecimal.ZERO : storedBalance);
        return map;
    }

    public Map<String, Object> consumeTypeRatio(String range, String start, String end, Long depotId) {
        DateRange dr = resolveRange(range, start, end);
        BigDecimal serviceAmount = customerDashboardMapperEx.sumServiceAmount(dr.startTime, dr.endTime, depotId);
        BigDecimal productAmount = customerDashboardMapperEx.sumProductAmount(dr.startTime, dr.endTime, depotId);
        Map<String, Object> map = new HashMap<>();
        map.put("serviceAmount", serviceAmount == null ? BigDecimal.ZERO : serviceAmount);
        map.put("productAmount", productAmount == null ? BigDecimal.ZERO : productAmount);
        return map;
    }

    public List<Map<String, Object>> revenueTrend(String range, String start, String end, Long depotId) {
        DateRange dr = resolveRange(range, start, end);
        return customerDashboardMapperEx.revenueTrend(dr.startTime, dr.endTime, depotId);
    }

    public List<Supplier> recentMembers(Integer limit) {
        return customerDashboardMapperEx.listRecentMembers(limit);
    }

    private static BigDecimal safeAdd(BigDecimal a, BigDecimal b) {
        BigDecimal x = a == null ? BigDecimal.ZERO : a;
        BigDecimal y = b == null ? BigDecimal.ZERO : b;
        return x.add(y);
    }

    private DateRange resolveRange(String range, String start, String end) {
        if (range == null || range.trim().isEmpty() || "today".equalsIgnoreCase(range)) {
            LocalDate d = LocalDate.now();
            return new DateRange(toDateTime(d.atStartOfDay()), toDateTime(d.plusDays(1).atStartOfDay()));
        }
        if ("month".equalsIgnoreCase(range)) {
            LocalDate d = LocalDate.now().withDayOfMonth(1);
            return new DateRange(toDateTime(d.atStartOfDay()), toDateTime(d.plusMonths(1).atStartOfDay()));
        }
        Date s = parseDateTime(start);
        Date e = parseDateTime(end);
        if (s == null && e == null) {
            LocalDate d = LocalDate.now();
            return new DateRange(toDateTime(d.atStartOfDay()), toDateTime(d.plusDays(1).atStartOfDay()));
        }
        if (s != null && e == null) {
            LocalDateTime st = toLocalDateTime(s);
            return new DateRange(s, toDateTime(st.plusDays(1)));
        }
        if (s == null) {
            LocalDateTime et = toLocalDateTime(e);
            return new DateRange(toDateTime(et.toLocalDate().atStartOfDay()), e);
        }
        return new DateRange(s, e);
    }

    private static Date parseDateTime(String s) {
        if (s == null) return null;
        String v = s.trim();
        if (v.isEmpty()) return null;
        try {
            if (v.length() == 10) {
                LocalDate d = LocalDate.parse(v, DateTimeFormatter.ofPattern("yyyy-MM-dd"));
                return toDateTime(d.atStartOfDay());
            }
            if (v.length() == 19) {
                LocalDateTime dt = LocalDateTime.parse(v, DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm:ss"));
                return toDateTime(dt);
            }
            LocalDateTime dt = LocalDateTime.parse(v, DateTimeFormatter.ISO_LOCAL_DATE_TIME);
            return toDateTime(dt);
        } catch (DateTimeParseException ex) {
            return null;
        }
    }

    private static Date toDateTime(LocalDateTime dt) {
        return Date.from(dt.atZone(ZoneId.systemDefault()).toInstant());
    }

    private static LocalDateTime toLocalDateTime(Date date) {
        return LocalDateTime.ofInstant(date.toInstant(), ZoneId.systemDefault());
    }

    private static final class DateRange {
        private final Date startTime;
        private final Date endTime;

        private DateRange(Date startTime, Date endTime) {
            this.startTime = startTime;
            this.endTime = endTime;
        }
    }
}

