package com.jsh.erp.service.cashier;

import com.alibaba.fastjson.JSONArray;
import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.datasource.entities.CashierSettlement;
import com.jsh.erp.datasource.entities.CashierSettlementPayment;
import com.jsh.erp.datasource.entities.CashierSession;
import com.jsh.erp.datasource.entities.ServiceOrder;
import com.jsh.erp.datasource.entities.ServiceOrderItem;
import com.jsh.erp.datasource.entities.Supplier;
import com.jsh.erp.datasource.mappers.CashierSettlementMapper;
import com.jsh.erp.datasource.mappers.CashierSettlementPaymentMapper;
import com.jsh.erp.datasource.mappers.InvoiceRequestMapper;
import com.jsh.erp.service.SupplierService;
import org.junit.Before;
import org.junit.Test;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.test.util.ReflectionTestUtils;

import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.math.BigDecimal;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import static org.junit.Assert.assertEquals;

public class CashierSettlementServiceCardTest {

    private CashierSettlementService cashierSettlementService;
    private CapturingSettlementMapperHandler settlementMapperHandler;
    private CapturingPaymentMapperHandler paymentMapperHandler;
    private CapturingInvoiceMapperHandler invoiceMapperHandler;
    private TestSupplierService supplierService;
    private TestCashierSessionService cashierSessionService;
    private TestServiceOrderService serviceOrderService;
    private TestCashierCartService cashierCartService;

    @Before
    public void setup() {
        settlementMapperHandler = new CapturingSettlementMapperHandler();
        paymentMapperHandler = new CapturingPaymentMapperHandler();
        invoiceMapperHandler = new CapturingInvoiceMapperHandler();

        CashierSettlementMapper settlementMapper = (CashierSettlementMapper) Proxy.newProxyInstance(
                CashierSettlementMapper.class.getClassLoader(),
                new Class[]{CashierSettlementMapper.class},
                settlementMapperHandler
        );
        CashierSettlementPaymentMapper paymentMapper = (CashierSettlementPaymentMapper) Proxy.newProxyInstance(
                CashierSettlementPaymentMapper.class.getClassLoader(),
                new Class[]{CashierSettlementPaymentMapper.class},
                paymentMapperHandler
        );
        InvoiceRequestMapper invoiceRequestMapper = (InvoiceRequestMapper) Proxy.newProxyInstance(
                InvoiceRequestMapper.class.getClassLoader(),
                new Class[]{InvoiceRequestMapper.class},
                invoiceMapperHandler
        );

        supplierService = new TestSupplierService();
        cashierSessionService = new TestCashierSessionService();
        serviceOrderService = new TestServiceOrderService();
        cashierCartService = new TestCashierCartService();

        cashierSettlementService = new CashierSettlementService();
        ReflectionTestUtils.setField(cashierSettlementService, "cashierSessionService", cashierSessionService);
        ReflectionTestUtils.setField(cashierSettlementService, "serviceOrderService", serviceOrderService);
        ReflectionTestUtils.setField(cashierSettlementService, "cashierCartService", cashierCartService);
        ReflectionTestUtils.setField(cashierSettlementService, "cashierSettlementMapper", settlementMapper);
        ReflectionTestUtils.setField(cashierSettlementService, "cashierSettlementPaymentMapper", paymentMapper);
        ReflectionTestUtils.setField(cashierSettlementService, "invoiceRequestMapper", invoiceRequestMapper);
        ReflectionTestUtils.setField(cashierSettlementService, "supplierService", supplierService);
    }

    @Test
    public void checkout_shouldValidateAndConsumeCardBalance_andReturnBalances() throws Exception {
        Long tenantId = 2L;
        Long sessionId = 1001L;
        Long memberId = 2001L;

        CashierSession session = new CashierSession();
        session.setId(sessionId);
        session.setStatus("OPEN");
        session.setDepotId(1L);
        session.setSeatId(1L);
        session.setMemberId(memberId);
        cashierSessionService.setSession(session);

        Supplier member = new Supplier();
        member.setId(memberId);
        member.setSupplier("M001");
        member.setTelephone("13800000000");
        member.setAdvanceIn(new BigDecimal("50.00"));
        cashierSessionService.setMember(member);

        supplierService.setUpdatedMemberBalance(new BigDecimal("30.00"));

        JSONObject obj = new JSONObject();
        obj.put("sessionId", sessionId);
        obj.put("clearSeat", false);
        obj.put("needInvoice", false);

        JSONArray payments = new JSONArray();
        JSONObject p1 = new JSONObject();
        p1.put("payMethod", "CARD");
        p1.put("amount", new BigDecimal("20.00"));
        JSONObject p2 = new JSONObject();
        p2.put("payMethod", "CASH");
        p2.put("amount", new BigDecimal("80.00"));
        payments.add(p1);
        payments.add(p2);
        obj.put("payments", payments);

        Map<String, Object> result = cashierSettlementService.checkout(obj, tenantId, 9L, new MockHttpServletRequest());

        assertEquals(new BigDecimal("20.00"), ((BigDecimal) result.get("prepaidUsedAmount")).setScale(2));
        assertEquals(new BigDecimal("50.00"), ((BigDecimal) result.get("balanceBefore")).setScale(2));
        assertEquals(new BigDecimal("30.00"), ((BigDecimal) result.get("balanceAfter")).setScale(2));
        assertEquals(Long.valueOf(1L), result.get("settlementId"));
        assertEquals("PAID", settlementMapperHandler.lastInserted.getStatus());
    }

    @Test(expected = RuntimeException.class)
    public void checkout_shouldRejectCardPaymentWithoutMember() throws Exception {
        Long tenantId = 2L;
        Long sessionId = 1002L;

        CashierSession session = new CashierSession();
        session.setId(sessionId);
        session.setStatus("OPEN");
        session.setDepotId(1L);
        session.setSeatId(1L);
        session.setMemberId(null);
        cashierSessionService.setSession(session);
        cashierSessionService.setMember(null);

        JSONObject obj = new JSONObject();
        obj.put("sessionId", sessionId);
        obj.put("clearSeat", false);
        obj.put("needInvoice", false);

        JSONArray payments = new JSONArray();
        JSONObject p1 = new JSONObject();
        p1.put("payMethod", "CARD");
        p1.put("amount", new BigDecimal("1.00"));
        payments.add(p1);
        obj.put("payments", payments);

        cashierSettlementService.checkout(obj, tenantId, 9L, new MockHttpServletRequest());
    }

    private static class CapturingSettlementMapperHandler implements InvocationHandler {
        private CashierSettlement lastInserted;

        @Override
        public Object invoke(Object proxy, Method method, Object[] args) {
            String name = method.getName();
            if ("selectLatestBySessionId".equals(name)) {
                return null;
            }
            if ("insertSelective".equals(name) && args != null && args.length == 1 && args[0] instanceof CashierSettlement) {
                lastInserted = (CashierSettlement) args[0];
                lastInserted.setId(1L);
                return 1;
            }
            Class<?> returnType = method.getReturnType();
            if (returnType.equals(int.class) || returnType.equals(Integer.class)) {
                return 0;
            }
            return null;
        }
    }

    private static class CapturingPaymentMapperHandler implements InvocationHandler {
        @Override
        public Object invoke(Object proxy, Method method, Object[] args) {
            String name = method.getName();
            if ("deleteBySettlementId".equals(name)) {
                return 1;
            }
            if ("insertSelective".equals(name) && args != null && args.length == 1 && args[0] instanceof CashierSettlementPayment) {
                return 1;
            }
            Class<?> returnType = method.getReturnType();
            if (returnType.equals(int.class) || returnType.equals(Integer.class)) {
                return 0;
            }
            return null;
        }
    }

    private static class CapturingInvoiceMapperHandler implements InvocationHandler {
        @Override
        public Object invoke(Object proxy, Method method, Object[] args) {
            Class<?> returnType = method.getReturnType();
            if (returnType.equals(int.class) || returnType.equals(Integer.class)) {
                return 0;
            }
            return null;
        }
    }

    private static class TestSupplierService extends SupplierService {
        private Supplier updatedMember;

        public void setUpdatedMemberBalance(BigDecimal balance) {
            Supplier s = new Supplier();
            s.setId(2001L);
            s.setAdvanceIn(balance);
            updatedMember = s;
        }

        @Override
        public Supplier getSupplier(long id) {
            return updatedMember;
        }

        @Override
        public void updateAdvanceIn(Long supplierId) {
        }
    }

    private static class TestCashierSessionService extends CashierSessionService {
        private CashierSession session;
        private Supplier member;

        public void setSession(CashierSession session) {
            this.session = session;
        }

        public void setMember(Supplier member) {
            this.member = member;
        }

        @Override
        public CashierSession ensureSessionPermission(Long sessionId, Long tenantId) {
            return session;
        }

        @Override
        public Map<String, Object> getDetail(Long sessionId, Long tenantId) {
            Map<String, Object> result = new HashMap<>();
            result.put("items", Collections.emptyList());
            result.put("session", session);
            result.put("seat", null);
            result.put("member", member);
            return result;
        }

        @Override
        public int closeSession(JSONObject obj, Long tenantId, javax.servlet.http.HttpServletRequest request) {
            return 1;
        }
    }

    private static class TestServiceOrderService extends ServiceOrderService {
        @Override
        public List<ServiceOrder> listBySessionId(Long sessionId, Long tenantId) {
            ServiceOrder order = new ServiceOrder();
            order.setId(3001L);
            return Collections.singletonList(order);
        }

        @Override
        public List<ServiceOrderItem> listItemsByOrderId(Long orderId, Long tenantId) {
            ServiceOrderItem item = new ServiceOrderItem();
            item.setAmount(new BigDecimal("100.00"));
            return Collections.singletonList(item);
        }
    }

    private static class TestCashierCartService extends CashierCartService {
        @Override
        public List<com.jsh.erp.datasource.entities.CashierSessionProductItem> listProductsBySessionId(Long sessionId, Long tenantId) {
            return Collections.emptyList();
        }
    }
}
